using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DiscordDmCleaner {
    public sealed class DiscordCooldown {
        public DateTime UntilUtc { get; private set; }
        internal void Extend(double seconds) {
            var now = DateTime.UtcNow;
            var deadline = seconds >= (DateTime.MaxValue - now).TotalSeconds - 1
                ? DateTime.MaxValue : now.AddSeconds(seconds);
            if (deadline > UntilUtc) UntilUtc = deadline;
        }
    }

    public class ApiFailure : Exception {
        public int Status;
        public int Code;
        public ApiFailure(int status, int code, string operation = null) : base(Describe(status, code, operation)) { Status = status; Code = code; }
        static string Describe(int status, int code, string operation) {
            string detail = status == 401 ? "Discord rejected the token." :
                code == 20002 ? "Discord restricts this request to bot accounts." :
                code == 50001 ? "Discord reports missing access." :
                code == 50013 ? "Discord reports missing permission." :
                code == 50003 ? "Discord does not allow this request in a DM." :
                code == 40002 ? "Discord requires account verification. Open Discord yourself to check." :
                status == 403 ? "Discord refused access." :
                status == 429 ? "Discord is still rate limiting requests." : "Discord refused the request.";
            return detail + " HTTP " + status + "; " + (code == 0 ? "no Discord error code" : "Discord code " + code) +
                (String.IsNullOrEmpty(operation) ? "" : "; step: " + operation) + ". Stopped.";
        }
    }

    // This client never reads Discord's files, process, screen, clipboard or saved login.
    // The caller supplies a token. Requests only go to Discord's HTTPS API, without redirects.
    public sealed class DiscordApi : IDisposable {
        readonly HttpClient http;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 16000000 };
        readonly Func<TimeSpan, CancellationToken, Task> delay;
        readonly DiscordCooldown cooldown;
        DateTime nextRequest = DateTime.MinValue;
        public Action<string> Status = delegate {};
        public DiscordApi(string token, DiscordCooldown cooldown = null) : this(token, new HttpClientHandler { AllowAutoRedirect = false },
            (span, ct) => Task.Delay(span, ct), cooldown) {}
        public DiscordApi(string token, HttpMessageHandler handler, Func<TimeSpan, CancellationToken, Task> wait, DiscordCooldown cooldown = null) {
            if (String.IsNullOrWhiteSpace(token) || token.IndexOfAny(new[] {'\r', '\n'}) >= 0)
                throw new ArgumentException("Enter a valid account token in the local window.");
            http = new HttpClient(handler) { BaseAddress = new Uri("https://discord.com/api/v10/"), Timeout = TimeSpan.FromSeconds(40) };
            http.DefaultRequestHeaders.Add("Authorization", token.Trim());
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DiscordDmCleaner/1.0.2");
            delay = wait;
            this.cooldown = cooldown ?? new DiscordCooldown();
        }
        static double Seconds(string value) {
            double n;
            return Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out n) && !Double.IsNaN(n) && !Double.IsInfinity(n) ? n : 0;
        }
        static string Header(HttpResponseMessage r, string key) {
            IEnumerable<string> values;
            return r.Headers.TryGetValues(key, out values) ? values.FirstOrDefault() : "";
        }
        static string Operation(HttpMethod method, string route) {
            if (route == "users/@me") return "check account";
            if (method == HttpMethod.Delete) return "delete message";
            if (route.Contains("/messages?")) return "read DM history";
            return "check DM recipient";
        }
        public async Task<object> Request(HttpMethod method, string route, CancellationToken ct) {
            if (route.StartsWith("/") || route.Contains(":") || route.Contains("..")) throw new ArgumentException("Invalid API route.");
            for (int attempt = 0; attempt < 5; attempt++) {
                ct.ThrowIfCancellationRequested();
                DateTime deadline = nextRequest;
                if (cooldown.UntilUtc > deadline && cooldown.UntilUtc > DateTime.UtcNow) {
                    deadline = cooldown.UntilUtc;
                    Status("Discord requested a pause until " + deadline.ToLocalTime().ToString("T") + ". Stop remains available.");
                }
                TimeSpan pause = deadline - DateTime.UtcNow;
                if (pause.TotalSeconds > 3601) throw new ApiFailure(429, 0, Operation(method, route));
                if (pause > TimeSpan.Zero) await delay(pause, ct);
                using (var request = new HttpRequestMessage(method, route))
                using (var response = await http.SendAsync(request, ct)) {
                    string body = await response.Content.ReadAsStringAsync();
                    object data = null;
                    if (!String.IsNullOrWhiteSpace(body)) { try { data = json.DeserializeObject(body); } catch (ArgumentException) {} }
                    var obj = data as Dictionary<string, object>;
                    if ((int)response.StatusCode == 429) {
                        double retry = Math.Max(Seconds(Header(response, "Retry-After")), obj != null && obj.ContainsKey("retry_after") ? Seconds(Convert.ToString(obj["retry_after"], CultureInfo.InvariantCulture)) : 0);
                        if (retry > 0) cooldown.Extend(retry + 0.25);
                        if (retry <= 0 || retry > 3600 || attempt == 4) throw new ApiFailure(429, 0, Operation(method, route));
                        continue;
                    }
                    nextRequest = DateTime.UtcNow.AddMilliseconds(650);
                    if (Header(response, "X-RateLimit-Remaining") == "0") {
                        double reset = Seconds(Header(response, "X-RateLimit-Reset-After"));
                        cooldown.Extend(Math.Max(0.65, reset + 0.25));
                    }
                    if (!response.IsSuccessStatusCode) {
                        int code = obj != null && obj.ContainsKey("code") ? Convert.ToInt32(obj["code"]) : 0;
                        throw new ApiFailure((int)response.StatusCode, code, Operation(method, route));
                    }
                    if (response.StatusCode != HttpStatusCode.NoContent && data == null)
                        throw new InvalidOperationException("Discord returned an unreadable response. Stopped.");
                    return data;
                }
            }
            throw new ApiFailure(429, 0);
        }
        public void Dispose() { http.DefaultRequestHeaders.Remove("Authorization"); http.Dispose(); }
    }

    public sealed class Plan {
        public string AccountId { get; internal set; }
        public string AccountName { get; internal set; }
        public string ChannelId { get; internal set; }
        public string RecipientId { get; internal set; }
        public string RecipientName { get; internal set; }
        public System.Collections.ObjectModel.ReadOnlyCollection<string> MessageIds { get; private set; }
        public int SystemMessages { get; internal set; }
        internal Plan(List<string> ids) { MessageIds = ids.AsReadOnly(); }
    }
    public sealed class DeleteProgress { public int Deleted, AlreadyGone; }

    public sealed class Cleaner {
        readonly DiscordApi api;
        public Action<string> Status = delegate {};
        public Cleaner(DiscordApi api) { this.api = api; }
        public static bool IsId(string value) {
            ulong parsed;
            return !String.IsNullOrEmpty(value) && value.All(c => c >= '0' && c <= '9') && UInt64.TryParse(value, out parsed) && parsed > 0;
        }
        public static string ChannelId(string value) {
            value = value.Trim();
            if (IsId(value)) return value;
            Uri uri;
            if (Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == "https" &&
                (uri.Host == "discord.com" || uri.Host == "ptb.discord.com" || uri.Host == "canary.discord.com")) {
                string[] parts = uri.AbsolutePath.Trim('/').Split('/');
                if ((parts.Length == 3 || parts.Length == 4) && parts[0] == "channels" && parts[1] == "@me" && IsId(parts[2])) return parts[2];
            }
            throw new ArgumentException("Enter a DM channel ID or a message link from that DM.");
        }
        static Dictionary<string, object> Obj(object value) {
            var obj = value as Dictionary<string, object>;
            if (obj == null) throw new InvalidOperationException("Unexpected Discord response. Stopped.");
            return obj;
        }
        static string Str(Dictionary<string, object> o, string key) {
            object value;
            return o.TryGetValue(key, out value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : "";
        }
        static int Type(Dictionary<string, object> o) {
            object value;
            if (!o.TryGetValue("type", out value) || !(value is int))
                throw new InvalidOperationException("Missing or invalid message or channel type. Stopped.");
            return (int)value;
        }
        static bool Deletable(int type) {
            return type == 0 || type == 19 || type == 20 || type == 23;
        }
        async Task<Plan> Identity(string channel, CancellationToken ct, List<string> ids) {
            if (!IsId(channel)) throw new ArgumentException("Invalid DM channel ID.");
            var me = Obj(await api.Request(HttpMethod.Get, "users/@me", ct));
            if (!IsId(Str(me, "id")) || (me.ContainsKey("bot") && Convert.ToBoolean(me["bot"])))
                throw new InvalidOperationException("A normal user account is required for your personal DM.");
            var dm = Obj(await api.Request(HttpMethod.Get, "channels/" + channel, ct));
            if (Str(dm, "id") != channel || Type(dm) != 1 || !dm.ContainsKey("recipients"))
                throw new InvalidOperationException("This is not a one-to-one DM. Server channels and group DMs are refused.");
            var recipients = dm["recipients"] as object[];
            if (recipients == null || recipients.Length != 1) throw new InvalidOperationException("Could not verify the DM recipient. Stopped.");
            var other = Obj(recipients[0]);
            if (!IsId(Str(other, "id"))) throw new InvalidOperationException("Invalid recipient ID. Stopped.");
            return new Plan(ids) { AccountId = Str(me, "id"), AccountName = Str(me, "username"), ChannelId = channel,
                RecipientId = Str(other, "id"), RecipientName = Str(other, "username") };
        }
        public async Task<Plan> Scan(string channel, CancellationToken ct) {
            var ids = new List<string>();
            var plan = await Identity(channel, ct, ids);
            string before = null;
            int scanned = 0;
            while (true) {
                ct.ThrowIfCancellationRequested();
                var page = await api.Request(HttpMethod.Get, "channels/" + channel + "/messages?limit=100" + (before == null ? "" : "&before=" + before), ct) as object[];
                if (page == null) throw new InvalidOperationException("Unexpected history response. Stopped.");
                if (page.Length == 0) break;
                ulong minimum = UInt64.MaxValue;
                var pageIds = new HashSet<string>();
                foreach (object item in page) {
                    var message = Obj(item);
                    string id = Str(message, "id");
                    if (!IsId(id) || Str(message, "channel_id") != channel || (before != null && UInt64.Parse(id) >= UInt64.Parse(before)))
                        throw new InvalidOperationException("History did not match the selected DM or pagination. Stopped.");
                    minimum = Math.Min(minimum, UInt64.Parse(id));
                    int type = Type(message);
                    if (!pageIds.Add(id)) continue;
                    scanned++;
                    if (Str(Obj(message["author"]), "id") == plan.AccountId) {
                        if (Deletable(type)) ids.Add(id); else plan.SystemMessages++;
                    }
                }
                before = minimum.ToString(CultureInfo.InvariantCulture);
                Status("Read " + scanned + " messages; found " + plan.MessageIds.Count + " of yours.");
                // Continue even after a short page, until Discord returns an empty page.
            }
            return plan;
        }
        public async Task Delete(Plan plan, DeleteProgress progress, CancellationToken ct) {
            var current = await Identity(plan.ChannelId, ct, new List<string>());
            if (current.AccountId != plan.AccountId || current.RecipientId != plan.RecipientId)
                throw new InvalidOperationException("The account or DM recipient changed. Scan again.");
            foreach (string id in plan.MessageIds) {
                ct.ThrowIfCancellationRequested();
                if (!IsId(id)) throw new InvalidOperationException("Invalid message ID. Stopped.");
                string route = "channels/" + plan.ChannelId + "/messages/" + id;
                try {
                    await api.Request(HttpMethod.Delete, route, ct);
                    progress.Deleted++;
                } catch (ApiFailure error) {
                    if (error.Status == 404 && error.Code == 10008) progress.AlreadyGone++;
                    else throw;
                }
                Status("Deleted " + progress.Deleted + "; already gone " + progress.AlreadyGone + "; scanned total " + plan.MessageIds.Count + ".");
            }
        }
    }

}
