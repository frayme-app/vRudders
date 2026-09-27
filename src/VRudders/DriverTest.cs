using System.Text;

namespace VRudders;

internal static class DriverTest
{
    // Run only while forwarding is stopped and before entering the game.
    public static string Run()
    {
        var log = new StringBuilder();
        var device = Joysticks.Enumerate().Single(d => d.IsVirtual);
        using var output = VirtualOutput.Connect();
        foreach (ushort expected in new ushort[] { 0, 16384, 32768, 49152, 65535 })
        {
            for (int n = 0; n < 10; n++) { output.Send(expected); Thread.Sleep(15); }
            AssertZ(device, expected);
            log.AppendLine($"PASS: driver → Windows joystick Z = {expected}");
        }
        if (output.TryReport([2, 99, 0, 0, 1])) throw new Exception("Driver accepted an unsupported protocol version.");
        if (output.TryReport([2, 1, 0, 0, 2])) throw new Exception("Driver accepted an invalid active flag.");
        log.AppendLine("PASS: invalid protocol and flags rejected");
        output.Send(65535);
        Thread.Sleep(500);
        AssertZ(device, 32768);
        log.AppendLine("PASS: lost heartbeat returns Z to centre");
        output.Send(0);
        output.Send(32768, false);
        Thread.Sleep(100);
        AssertZ(device, 32768);
        log.AppendLine("PASS: stop returns Z to centre");
        return log.ToString();
    }
    private static void AssertZ(Joystick device, ushort expected)
    {
        if (!Joysticks.TryRead(device.Id, out var p)) throw new Exception("Virtual device disappeared.");
        if (Math.Abs((long)p.Z - expected) > 2) throw new Exception($"Expected Z={expected}; Windows read {p.Z}.");
        if (Math.Abs((long)p.X - 32768) > 2 || Math.Abs((long)p.Y - 32768) > 2 || p.Buttons != 0)
            throw new Exception("Unrelated axes/buttons were not neutral.");
    }
}
