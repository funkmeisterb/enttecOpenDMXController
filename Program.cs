namespace dmxEasyDancing
{
    class Program
    {
        static void Main(string[] args)
        {
            if (!EnttecOpenDmx.Start())
            {
                Console.WriteLine("Could not connect to Enttec OpenDMX USB device.");
                Console.WriteLine("Press any key to exit.");
                Console.ReadKey();
                return;
            }

            Console.WriteLine("Enttec OpenDMX USB device connected.");
            Console.WriteLine("Starting Gain+RGBW test pattern... Press Ctrl+C to exit.");

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true; // Prevent the process from terminating immediately
                EnttecOpenDmx.Stop();
                Console.WriteLine("\nEnttec OpenDMX USB device stopped.");
                Environment.Exit(0);
            };

            while (true)
            {
                Console.WriteLine("Red (Gain: 255, R: 255, G: 0, B: 0, W: 0)");
                SetPixelColor(pixelIndex: 0, gain: 255, r: 255, g: 0, b: 0, w: 0);
                Thread.Sleep(1000);

                Console.WriteLine("Green (Gain: 255, R: 0, G: 255, B: 0, W: 0)");
                SetPixelColor(pixelIndex: 0, gain: 255, r: 0, g: 255, b: 0, w: 0);
                Thread.Sleep(1000);

                Console.WriteLine("Blue (Gain: 255, R: 0, G: 0, B: 255, W: 0)");
                SetPixelColor(pixelIndex: 0, gain: 255, r: 0, g: 0, b: 255, w: 0);
                Thread.Sleep(1000);

                Console.WriteLine("White (Gain: 255, R: 0, G: 0, B: 0, W: 255)");
                SetPixelColor(pixelIndex: 0, gain: 255, r: 0, g: 0, b: 0, w: 255);
                Thread.Sleep(1000);
            }
        }

        /// <summary>
        /// Sets a pixel with the format: Gain, Red, Green, Blue, White (5 channels per pixel).
        /// </summary>
        /// <param name="pixelIndex">0-based index of the pixel.</param>
        /// <param name="gain">Gain / Master brightness (0-255).</param>
        /// <param name="r">Red intensity (0-255).</param>
        /// <param name="g">Green intensity (0-255).</param>
        /// <param name="b">Blue intensity (0-255).</param>
        /// <param name="w">White intensity (0-255).</param>
        /// <param name="startChannel">Starting DMX address for pixel 0 (defaults to 1).</param>
        static void SetPixelColor(int pixelIndex, byte gain, byte r, byte g, byte b, byte w, int startChannel = 1)
        {
            int baseChannel = startChannel + (pixelIndex * 5);
            EnttecOpenDmx.SetDmxValue(baseChannel, gain);     // Gain
            EnttecOpenDmx.SetDmxValue(baseChannel + 1, r);     // Red
            EnttecOpenDmx.SetDmxValue(baseChannel + 2, g);     // Green
            EnttecOpenDmx.SetDmxValue(baseChannel + 3, b);     // Blue
            EnttecOpenDmx.SetDmxValue(baseChannel + 4, w);     // White
        }
    }
}
