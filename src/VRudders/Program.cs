using System.Diagnostics;
using System.Text.Json;

namespace VRudders;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--list"))
        {
            var devices = Joysticks.Enumerate().Select(d => new { d.Id, d.Name, d.Vendor, d.Product, d.IsVirtual, d.Backend, d.Key, d.InputError, d.Axes, Position = Read(d) });
            string json = JsonSerializer.Serialize(devices, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(json);
            if (args.Length > 1) File.WriteAllText(args[1], json);
            return 0;
        }
        if (args.Contains("--self-test"))
        {
            try { File.WriteAllText(args.Length > 1 ? args[1] : "self-test.txt", SelfTests.Run()); return 0; }
            catch (Exception ex) { File.WriteAllText(args.Length > 1 ? args[1] : "self-test.txt", "FAIL: " + ex); return 1; }
        }
        // Read-only diagnostics and screenshots never write to the virtual controller.
        bool screenshot = args.Length == 2 && args[0] is "--screenshot" or "--screenshot-tuning" or "--screenshot-calibration";
        bool readOnly = screenshot || (args.Length == 2 && args[0] == "--ui-self-test");
        using var instance = readOnly ? null : InstanceGuard.TryAcquire(out _);
        if (instance == null && !readOnly)
        {
            const string message = "VRudders is already running. Close the existing copy before starting another or running a driver test.";
            if (args.Length == 2 && args[0] == "--driver-test") File.WriteAllText(args[1], "FAIL: " + message);
            else if (args.Length == 0) MessageBox.Show(message, "VRudders", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 17;
        }
        if (args.Length == 2 && args[0] == "--ui-self-test")
        {
            string folder = Path.Combine(Path.GetTempPath(), "VRudders-ui-test-" + Guid.NewGuid().ToString("N"));
            SettingsStore.TestDirectory = folder;
            try
            {
                ApplicationConfiguration.Initialize();
                using var testForm = new MainForm { Opacity = 0, ShowInTaskbar = false };
                testForm.Show(); Application.DoEvents(); testForm.VerifyEditorWorkflow(); testForm.Close();
                MainForm.VerifyForwardingWorkflow();
                File.WriteAllText(args[1], "PASS: protected bypass, new profile defaults, linked endpoints, edit/apply/save/reset, settings round-trip, disconnect identity checks, manual resume; slow forwarding startup, fresh input, real pause/neutral, repeated Start/Stop, input lost during connection. Isolated temporary settings and simulated forwarding; no driver writes.");
                return 0;
            }
            catch (Exception ex) { File.WriteAllText(args[1], "FAIL: " + ex); return 1; }
            finally
            {
                if (File.Exists(Path.Combine(folder, "settings.json"))) File.Delete(Path.Combine(folder, "settings.json"));
                if (Directory.Exists(folder)) Directory.Delete(folder);
                SettingsStore.TestDirectory = null;
            }
        }
        if (args.Length == 1 && args[0] == "--instance-probe") return 0;
        if (args.Length == 2 && args[0] == "--driver-test")
        {
            try { File.WriteAllText(args[1], DriverTest.Run()); return 0; }
            catch (Exception ex) { File.WriteAllText(args[1], "FAIL: " + ex); return 1; }
        }
        ApplicationConfiguration.Initialize();
        using var form = new MainForm();
        if (screenshot)
        {
            form.Opacity = 0; form.ShowInTaskbar = false; form.Show();
            form.SelectPreviewPage(args[0] == "--screenshot-tuning" ? 1 : args[0] == "--screenshot-calibration" ? 2 : 0);
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 250) { Application.DoEvents(); Thread.Sleep(5); }
            using var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, form.ClientRectangle with { Width = form.Width, Height = form.Height });
            bmp.Save(args[1]); return 0;
        }
        Application.Run(form);
        return 0;
    }
    static object? Read(Joystick d)
    {
        if (d.Axes.Length == 0) return null;
        try
        {
            using var reader = d.OpenReader();
            // Give DirectInput's initial input notifications time to reach this STA.
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 100) { Application.DoEvents(); Thread.Sleep(5); }
            return reader.TryRead(out var p) ? new { p.X, p.Y, p.Z, p.R, p.U, p.V } : null;
        }
        catch (Exception ex) { return new { Error = ex.Message }; }
    }
}
