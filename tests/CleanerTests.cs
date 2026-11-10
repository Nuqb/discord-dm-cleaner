using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using DiscordDmCleaner;

class FakeDiscord : HttpMessageHandler {
    public string Owner = "100", Recipient = "200";
    public int ChannelType = 1;
    public int PageCalls, Deletes, Requests, RateLimitOnce, SingleMessageGets;
    public bool ChangedAuthor, MissingMessage, MissingChannel, CancelAfterFirst, ReturnNeighbor, RefuseDelete;
    public CancellationTokenSource CancelSource;
    public readonly List<string> DeletedIds = new List<string>();
    public readonly List<double> Delays = new List<double>();
    public Func<string, object[]> Page;
    readonly JavaScriptSerializer json = new JavaScriptSerializer();
    public static Dictionary<string,object> Message(string id, string author, int type) {
        return new Dictionary<string,object> { { "id", id }, { "channel_id", "300" }, { "type", type },
            { "author", new Dictionary<string,object> { { "id", author } } }, { "content", "test" } };
    }
    HttpResponseMessage Reply(int code, object data) {
        return new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent(data == null ? "" : json.Serialize(data)) };
    }
    public Task Wait(TimeSpan span, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Delays.Add(span.TotalSeconds); return Task.FromResult(0); }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) {
        ct.ThrowIfCancellationRequested(); Requests++;
        if (req.RequestUri.Host != "discord.com" || req.RequestUri.Scheme != "https") throw new Exception("Unexpected destination");
        string route = req.RequestUri.PathAndQuery.Replace("/api/v10/", "");
        if (RateLimitOnce > 0) { RateLimitOnce--; return Task.FromResult(Reply(429, new { retry_after = 2.5 })); }
        if (route == "users/@me") return Task.FromResult(Reply(200, new { id = Owner, username = "myself", bot = false }));
        if (route == "channels/300") return Task.FromResult(Reply(200, new { id = "300", type = ChannelType, recipients = new[] { new { id = Recipient, username = "friend" } } }));
        if (route.StartsWith("channels/300/messages?")) {
            if (route.Contains("around=")) {
                if (MissingChannel) return Task.FromResult(Reply(404, new { code = 10003 }));
                if (MissingMessage) return Task.FromResult(Reply(200, new object[0]));
                string target = route.Split(new[] { "around=" }, StringSplitOptions.None)[1].Split('&')[0];
                return Task.FromResult(Reply(200, new object[] { Message(ReturnNeighbor ? "500" : target, ChangedAuthor ? "200" : "100", 0) }));
            }
            PageCalls++;
            return Task.FromResult(Reply(200, Page != null ? Page(route) :
                (PageCalls == 1 ? new object[] { Message("900", "100", 0), Message("800", "200", 0), Message("700", "100", 19), Message("600", "100", 3) } : new object[0])));
        }
        if (route.StartsWith("channels/300/messages/")) {
            if (MissingChannel) return Task.FromResult(Reply(404, new { code = 10003 }));
            if (MissingMessage) return Task.FromResult(Reply(404, new { code = 10008 }));
            string id = route.Split('/').Last();
            if (req.Method == HttpMethod.Delete) {
                if (RefuseDelete) return Task.FromResult(Reply(403, new { code = 50013 }));
                Deletes++; DeletedIds.Add(id);
                if (CancelAfterFirst) CancelSource.Cancel();
                return Task.FromResult(Reply(204, null));
            }
            SingleMessageGets++;
            return Task.FromResult(Reply(403, new { code = 20002 }));
        }
        throw new Exception("Unexpected route " + route);
    }
}

class CleanerTests {
    static int passed;
    static IEnumerable<System.Windows.Forms.Control> Descendants(System.Windows.Forms.Control parent) {
        foreach(System.Windows.Forms.Control child in parent.Controls) { yield return child; foreach(var nested in Descendants(child)) yield return nested; }
    }
    static void PumpUntil(Func<bool> condition) {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while(!condition() && timer.ElapsedMilliseconds < 2000) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(10); }
        Check(condition(), "UI transition did not finish");
    }
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); }
    static async Task ExpectFailure(Func<Task> action) { bool failed = false; try { await action(); } catch (InvalidOperationException) { failed = true; } Check(failed, "Expected a safety stop"); }
    static async Task Run(string name, Func<Task> test) { await test(); passed++; Console.WriteLine("PASS " + name); }
    static async Task All() {
        await Run("DM ID and link validation; server and untrusted links refused", async delegate {
            Check(Cleaner.ChannelId("300") == "300", "ID");
            Check(Cleaner.ChannelId("https://discord.com/channels/@me/300/900") == "300", "Link");
            foreach (string bad in new[] { "0", "../300", "https://discord.com/channels/999/300/900", "https://example.com/channels/@me/300", "https://discord.com.evil/channels/@me/300", "https://discord.com/channels/@me/300/900/extra" }) {
                bool rejected = false; try { Cleaner.ChannelId(bad); } catch (ArgumentException) { rejected = true; } Check(rejected, "Bad link accepted");
            }
            await Task.FromResult(0);
        });
        await Run("Scan is read-only, filters other authors and system entries, paginates past short pages", async delegate {
            var fake = new FakeDiscord();
            fake.Page = route => route.Contains("before=600") ? new object[] { FakeDiscord.Message("500", "100", 0) } :
                route.Contains("before=500") ? new object[0] : new object[] { FakeDiscord.Message("900", "100", 0), FakeDiscord.Message("800", "200", 0), FakeDiscord.Message("700", "100", 19), FakeDiscord.Message("600", "100", 3) };
            using (var api = new DiscordApi("FAKE-NOT-A-REAL-TOKEN", fake, fake.Wait)) {
                var p = await new Cleaner(api).Scan("300", CancellationToken.None);
                Check(p.MessageIds.SequenceEqual(new[] { "900", "700", "500" }), "Author filter or pagination");
                Check(p.SystemMessages == 1 && fake.Deletes == 0 && fake.PageCalls == 3, "Read-only scan");
            }
        });
        await Run("Server channels and group DMs rejected before history is read", async delegate {
            foreach (int type in new[] { 0, 3 }) {
                var fake = new FakeDiscord { ChannelType = type };
                using (var api = new DiscordApi("FAKE", fake, fake.Wait)) {
                    await ExpectFailure(async () => { await new Cleaner(api).Scan("300", CancellationToken.None); });
                    Check(fake.PageCalls == 0 && fake.Deletes == 0, "Non-DM read or delete");
                }
            }
        });
        await Run("Only scanned, reverified own messages are deleted", async delegate {
            var fake = new FakeDiscord();
            using (var api = new DiscordApi("FAKE", fake, fake.Wait)) {
                var cleaner = new Cleaner(api); var p = await cleaner.Scan("300", CancellationToken.None); var count = new DeleteProgress();
                await cleaner.Delete(p, count, CancellationToken.None);
                Check(fake.DeletedIds.SequenceEqual(new[] { "900", "700" }) && count.Deleted == 2, "Deletion scope");
                Check(fake.SingleMessageGets == 0, "Bot-only lookup was used");
            }
        });
        await Run("Changed account or recipient invalidates the deletion plan", async delegate {
            foreach (bool changeOwner in new[] { true, false }) {
                var fake = new FakeDiscord();
                using (var api = new DiscordApi("FAKE", fake, fake.Wait)) {
                    var cleaner = new Cleaner(api); var p = await cleaner.Scan("300", CancellationToken.None);
                    if (changeOwner) fake.Owner = "400"; else fake.Recipient = "400";
                    await ExpectFailure(() => cleaner.Delete(p, new DeleteProgress(), CancellationToken.None));
                    Check(fake.Deletes == 0, "Changed identity deleted");
                }
            }
        });
        await Run("Message ownership mismatch stops before any DELETE", async delegate {
            var fake = new FakeDiscord();
            using (var api = new DiscordApi("FAKE", fake, fake.Wait)) {
                var cleaner = new Cleaner(api); var p = await cleaner.Scan("300", CancellationToken.None); fake.ChangedAuthor = true;
                await ExpectFailure(() => cleaner.Delete(p, new DeleteProgress(), CancellationToken.None)); Check(fake.Deletes == 0, "Wrong author deleted");
            }
        });
        await Run("Rate limit retry honors Discord's requested wait", async delegate {
            var fake = new FakeDiscord { RateLimitOnce = 1 };
            using (var api = new DiscordApi("FAKE", fake, fake.Wait)) { await new Cleaner(api).Scan("300", CancellationToken.None); Check(fake.Delays.Any(n => n >= 2.5), "No retry wait"); }
        });
        await Run("Cancellation stops the batch after an in-flight deletion", async delegate {
            var fake = new FakeDiscord();
            using (var api = new DiscordApi("FAKE", fake, fake.Wait)) {
                var cleaner = new Cleaner(api); var p = await cleaner.Scan("300", CancellationToken.None);
                using (var ct = new CancellationTokenSource()) {
                    fake.CancelAfterFirst = true; fake.CancelSource = ct;
                    bool stopped = false; try { await cleaner.Delete(p, new DeleteProgress(), ct.Token); } catch (OperationCanceledException) { stopped = true; }
                    Check(stopped && fake.Deletes == 1, "Cancellation failed");
                }
            }
        });
        await Run("Already deleted messages skipped; missing channel stops", async delegate {
            var fake = new FakeDiscord();
            using (var api = new DiscordApi("FAKE", fake, fake.Wait)) {
                var cleaner = new Cleaner(api); var p = await cleaner.Scan("300", CancellationToken.None); fake.MissingMessage = true;
                var count = new DeleteProgress(); await cleaner.Delete(p, count, CancellationToken.None);
                Check(count.UnverifiedSkipped == 2 && fake.Deletes == 0, "Missing message");
                fake.MissingMessage = false; fake.MissingChannel = true;
                bool stopped = false; try { await cleaner.Delete(p, new DeleteProgress(), CancellationToken.None); } catch (ApiFailure) { stopped = true; }
                Check(stopped, "Missing channel ignored");
            }
        });
        await Run("Repeated history pages fail closed instead of looping", async delegate {
            var fake = new FakeDiscord { Page = route => new object[] { FakeDiscord.Message("900", "100", 0) } };
            using (var api = new DiscordApi("FAKE", fake, fake.Wait)) {
                await ExpectFailure(async () => { await new Cleaner(api).Scan("300", CancellationToken.None); }); Check(fake.Deletes == 0, "Scan deleted");
            }
        });
        await Run("Nearby messages cannot substitute for the exact verified target", async delegate {
            var fake = new FakeDiscord();
            using (var api = new DiscordApi("FAKE", fake, fake.Wait)) {
                var cleaner = new Cleaner(api); var p = await cleaner.Scan("300", CancellationToken.None); fake.ReturnNeighbor = true;
                var count = new DeleteProgress(); await cleaner.Delete(p, count, CancellationToken.None);
                Check(count.UnverifiedSkipped == 2 && fake.Deletes == 0, "Neighbor substituted");
            }
        });
        await Run("Deletion refusal reports code and step without disclosing token or IDs", async delegate {
            var fake = new FakeDiscord();
            using (var api = new DiscordApi("PRIVATE-FAKE-TOKEN", fake, fake.Wait)) {
                var cleaner = new Cleaner(api); var p = await cleaner.Scan("300", CancellationToken.None); fake.RefuseDelete = true;
                bool failed = false;
                try { await cleaner.Delete(p, new DeleteProgress(), CancellationToken.None); }
                catch (ApiFailure error) {
                    failed = true;
                    Check(error.Message.Contains("50013") && error.Message.Contains("delete message") && error.Message.Contains("403"), "Diagnostic missing");
                    Check(!error.Message.Contains("PRIVATE-FAKE-TOKEN") && !error.Message.Contains("900") && !error.Message.Contains("300"), "Private data in diagnostic");
                }
                Check(failed && fake.Deletes == 0, "Refusal did not stop");
            }
        });
        await Run("Desktop window constructs and renders without Discord access", async delegate {
            using (var form = new CleanerWindow()) {
                form.ShowInTaskbar = false;
                form.Opacity = 0;
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height)) {
                    form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0,0,form.Width,form.Height));
                    var colors = new HashSet<int>();
                    for (int y = 0; y < bitmap.Height; y += 3)
                        for (int x = 0; x < bitmap.Width; x += 3) colors.Add(bitmap.GetPixel(x, y).ToArgb());
                    Check(colors.Count > 20, "Window did not render");
                    bitmap.Save("artifacts/test-output/cleaner-preview.png", System.Drawing.Imaging.ImageFormat.Png);
                }
                form.Close();
                using (var icon = Theme.MakeIcon()) using (var file = System.IO.File.Create("artifacts/test-output/cleaner.ico")) icon.Save(file);
            }
            using (var dialog = new ConfirmDeleteDialog(new Plan { AccountName = "yourname", AccountId = "123456789012345678", RecipientName = "friend", RecipientId = "234567890123456789", ChannelId = "345678901234567890" })) {
                dialog.ShowInTaskbar = false; dialog.Opacity = 0; dialog.Show();
                System.Windows.Forms.Application.DoEvents();
                using (var bitmap = new System.Drawing.Bitmap(dialog.Width,dialog.Height)) {
                    dialog.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,dialog.Width,dialog.Height));
                    bitmap.Save("artifacts/test-output/confirmation-preview.png",System.Drawing.Imaging.ImageFormat.Png);
                }
                dialog.Close();
            }
            await Task.FromResult(0);
        });
        await Run("Setup guide retains entered values, updates the main fields, and animates opening and closing", async delegate {
            string copiedToken = null, copiedLink = null;
            using(var guide = new SetupGuideDialog("", "", value => copiedToken = value, value => copiedLink = value)) {
                guide.Show(); PumpUntil(() => guide.TransitionRunning);
                PumpUntil(() => !guide.TransitionRunning); Check(guide.Opacity == 1,"Guide did not fully open");
                for(int page = 0; page < 5; page++) {
                    guide.GoTo(page); Check(guide.Page == page,"Navigation failed");
                    if(page == 2) Descendants(guide).OfType<System.Windows.Forms.TextBox>().Single().Text = "FAKE-TEST-VALUE";
                    if(page == 3) Descendants(guide).OfType<System.Windows.Forms.TextBox>().Single().Text = "https://discord.com/channels/@me/300/900";
                    System.Windows.Forms.Application.DoEvents();
                    using(var bitmap = new System.Drawing.Bitmap(guide.Width,guide.Height)) {
                        guide.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,guide.Width,guide.Height));
                        bitmap.Save("artifacts/test-output/help-page-" + (page+1) + ".png",System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                guide.GoTo(2);
                var tokenBox = Descendants(guide).OfType<System.Windows.Forms.TextBox>().Single();
                Check(tokenBox.Text == "FAKE-TEST-VALUE" && tokenBox.UseSystemPasswordChar,"Token was lost or unmasked");
                Check(copiedToken == "FAKE-TEST-VALUE" && copiedLink == "https://discord.com/channels/@me/300/900","Setup values did not propagate");
                guide.Close(); Check(guide.Visible && guide.TransitionRunning,"Close skipped the transition");
                PumpUntil(() => !guide.Visible);
            }
            await Task.FromResult(0);
        });
        await Run("Actual help button opens and closes a modal guide and restores the main window", async delegate {
            using(var main = new CleanerWindow())
            using(var guard = new System.Windows.Forms.Timer { Interval = 15 }) {
                main.ShowInTaskbar = false; main.Opacity = 0; main.Show(); System.Windows.Forms.Application.DoEvents();
                bool seen = false; int ticks = 0;
                guard.Tick += delegate {
                    ticks++;
                    var active = System.Windows.Forms.Application.OpenForms.OfType<SetupGuideDialog>().FirstOrDefault();
                    if(active != null && active.Visible) {
                        seen = true;
                        if(!active.TransitionRunning) active.Close();
                    }
                    if(ticks > 160) { guard.Stop(); throw new Exception("Modal help did not finish"); }
                };
                guard.Start();
                try { Descendants(main).OfType<System.Windows.Forms.Button>().Single(b => b.AccessibleName == "Open the step-by-step setup guide").PerformClick(); }
                finally { guard.Stop(); }
                Check(seen && main.Enabled,"Help did not restore main window");
                Check(!System.Windows.Forms.Application.OpenForms.OfType<GuideShade>().Any(),"Dim overlay was left open");
                main.Close();
            }
            await Task.FromResult(0);
        });
        Console.WriteLine(passed + " tests passed. All API responses were simulated; no real account or messages were accessed.");
    }
    [STAThread] public static int Main() { try { System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.ThrowException); All().GetAwaiter().GetResult(); return 0; } catch (Exception e) { Console.Error.WriteLine(e); return 1; } }
}
