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
            Console.WriteLine("Starting RGBW test pattern... Press Ctrl+C to exit.");

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true; // Prevent the process from terminating immediately
                EnttecOpenDmx.Stop();
                Console.WriteLine("\nEnttec OpenDMX USB device stopped.");
                Environment.Exit(0);
            };

            // Set Gain (Channel 1) to full
            EnttecOpenDmx.SetDmxValue(1, 255);

            while (true)
            {
                Console.WriteLine("Red");
                SetColor(255, 0, 0, 0);
                Thread.Sleep(1000);

                Console.WriteLine("Green");
                SetColor(0, 255, 0, 0);
                Thread.Sleep(1000);

                Console.WriteLine("Blue");
                SetColor(0, 0, 255, 0);
                Thread.Sleep(1000);

                Console.WriteLine("White");
                SetColor(0, 0, 0, 255);
                Thread.Sleep(1000);
            }
        }

        static void SetColor(byte r, byte g, byte b, byte w)
        {
            EnttecOpenDmx.SetDmxValue(2, r); // Red
            EnttecOpenDmx.SetDmxValue(3, g); // Green
            EnttecOpenDmx.SetDmxValue(4, b); // Blue
            EnttecOpenDmx.SetDmxValue(5, w); // White
        }
    }
}
