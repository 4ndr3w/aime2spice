using Haruka.Arcade.SEGA835Lib.Debugging;
using Haruka.Arcade.SEGA835Lib.Devices;
using Haruka.Arcade.SEGA835Lib.Devices.Card._837_15396;
using Haruka.Arcade.SEGA835Lib.Devices.LED._837_15093;
using Haruka.Arcade.SEGA835Lib.Devices.Misc;
using Haruka.Arcade.SEGA835Lib.Misc;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

class Program
{
    static async Task Main(string[] args)
    {

        if (args.Length == 0)
        {
            await new Program().Run();
        }
        else if (args[0] == "led")
        {
            await new Program().Lights();
        }
        else
        {
            Console.Write("unknown");
        }
    }

    public Program()
    {

    }

    public async Task Lights()
    {
        Log.Mute = true;

        // COM5 on FGO, ? on APM3
        var lights = new LED_837_15093_06(5);
        lights.Connect();
        lights.Reset();
        float offset = 0;

        // 60Hz
        const int targetFps = 60;
        const int frameDelayMs = 1000 / targetFps;

        var ledCount = 25;

        lights.SetLEDCount(ledCount);
        lights.SetResponseDisabled(true);

        while (true)
        {
            Color[] colors = new Color[ledCount];
            for (int i = 0; i < colors.Length; ++i)
            {
                float hue = (colors.Length + offset) % 1.0f;
                colors[i] = HsvToRgb(hue, 1.0f, 1.0f);
            }
            lights.SetLEDs(colors);

            offset += 0.001f;
            if (offset > 1.0f) offset -= 1.0f;

            await Task.Delay(frameDelayMs);
        }
    }

    private static Color HsvToRgb(float h, float s, float v)
    {
        float r, g, b;

        int hi = (int)(h * 6) % 6;
        float f = h * 6 - (int)(h * 6);
        float p = v * (1 - s);
        float q = v * (1 - f * s);
        float t = v * (1 - (1 - f) * s);

        switch (hi)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }

        byte rb = (byte)Math.Clamp(r * 255, 0, 255);
        byte gb = (byte)Math.Clamp(g * 255, 0, 255);
        byte bb = (byte)Math.Clamp(b * 255, 0, 255);

        return Color.FromArgb(rb, gb, bb);
    }

    public async
    Task
Run()
    {
        // COM1 on APM3 and FGO
        var vfd = new VFD_GP1232A02A(1);
        vfd.Connect();
        vfd.Reset();
        vfd.SetOn(true);
        vfd.SetEncoding(VFDEncoding.SHIFT_JIS);

        Console.WriteLine("Initializing card reader...");
        // COM3 on APM3 and FGO
        var reader = new AimeCardReader_837_15396(3);
        reader.FlashLEDsWhilePolling = false;

        if (reader.Connect() != DeviceStatus.OK)
        {
            return;
        }
        reader.Reset();
        reader.LEDReset();
        reader.LEDSetColor(100, 100, 100);

        reader.RadioOn(RadioOnType.FeliCa);

        Log.Mute = true;

        while (true)
        {
            reader.Poll();
            var uid = reader.GetCardUID();
            if (uid != null)
            {
                reader.LEDSetColor(0, 255, 0);
                string decimalUid = ConvertToDecimalString(uid);
                string hexUid = ConvertToHexString(uid);

                Console.WriteLine($"Card detected! UID: {decimalUid} (Hex: {hexUid})");

                try
                {
                    await this.SpiceInsertTcp(hexUid, "127.0.0.1", 1337);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
                reader.ClearCard();
            }
            else
            {
                reader.LEDSetColor(100, 100, 100);
            }
            await Task.Delay(100); // Poll every 100ms
        }
    }

    private static string ConvertToDecimalString(byte[] uid)
    {
        System.Numerics.BigInteger number = new System.Numerics.BigInteger(uid.Reverse().ToArray());
        var s = number.ToString();
        while (s.Length < 20) { s = '0' + s; }
        return s;
    }

    private static string ConvertToHexString(byte[] uid)
    {
        return BitConverter.ToString(uid).Replace("-", "");
    }


    ulong idx = 0;

    private async Task SpiceInsertTcp(string cardId, string host, int apiPort)
    {
        var request = new SpiceRequest
        {
            Id = idx++,
            Module = "card",
            Function = "insert",
            Params = new object[] { 0, cardId }
        };

        using var client = new TcpClient();
        await client.ConnectAsync(host, apiPort);

        using var stream = client.GetStream();

        var jsonString = JsonSerializer.Serialize(request);
        Console.WriteLine($"Sending: {jsonString}");

        var payload = Encoding.UTF8.GetBytes(jsonString + "\0");

        await stream.WriteAsync(payload);

        // Read response
        var buffer = new byte[4096];
        using var ms = new MemoryStream();

        await stream.FlushAsync();
        stream.Close();
    }
}

internal class SpiceRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public ulong Id { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("module")]
    public required string Module { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("function")]
    public required string Function { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("params")]
    public required object[] Params { get; set; }
}
