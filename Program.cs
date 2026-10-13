using System;
using System.Threading;
using System.Threading.Tasks;

namespace dmxEasyDancing
{
    class Program
    {
        private static readonly WebServer.AppState _state = new()
        {
            Gain = 255,
            R = 0,
            G = 0,
            B = 0,
            W = 0,
            PatternRunning = false
        };

        private static CancellationTokenSource? _patternCts;
        private static readonly object _patternLock = new();
        private static WebServer? _webServer;

        static void Main(string[] args)
        {
            if (!EnttecOpenDmx.Start())
            {
                Console.WriteLine("Could not connect to Enttec OpenDMX USB device.");
                Console.WriteLine("Press any key to exit.");
                Console.ReadKey();
                return;
            }

            // Default all channels to off (0) on startup
            SetPixelColor(pixelIndex: 0, gain: 0, r: 0, g: 0, b: 0, w: 0);

            int port = 5000;
            _webServer = new WebServer(
                port,
                onColorChanged: OnColorChanged,
                onPatternToggle: OnPatternToggle,
                getState: () => _state);

            _webServer.Start();

            Console.WriteLine("==================================================");
            Console.WriteLine("Enttec OpenDMX USB device connected.");
            Console.WriteLine($"Web Controller listening on: http://localhost:{port}/");
            Console.WriteLine("Open the URL in any browser to control lights.");
            Console.WriteLine("Press Ctrl+C to exit.");
            Console.WriteLine("==================================================");

            var resetEvent = new ManualResetEvent(false);
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("\nShutting down Enttec OpenDMX & Web server...");
                StopPattern();
                _webServer.Stop();
                // Turn lights off
                SetPixelColor(pixelIndex: 0, gain: 0, r: 0, g: 0, b: 0, w: 0);
                EnttecOpenDmx.Stop();
                Console.WriteLine("Stopped.");
                resetEvent.Set();
            };

            resetEvent.WaitOne();
        }

        private static void OnColorChanged(byte gain, byte r, byte g, byte b, byte w)
        {
            // If manual color changed, disable active test pattern
            if (_state.PatternRunning)
            {
                StopPattern();
                _state.PatternRunning = false;
            }

            _state.Gain = gain;
            _state.R = r;
            _state.G = g;
            _state.B = b;
            _state.W = w;

            SetPixelColor(0, gain, r, g, b, w);
        }

        private static void OnPatternToggle(bool enabled)
        {
            if (enabled)
            {
                StartPattern();
            }
            else
            {
                StopPattern();
                // Restore manual state color
                SetPixelColor(0, _state.Gain, _state.R, _state.G, _state.B, _state.W);
            }
        }

        private static void StartPattern()
        {
            lock (_patternLock)
            {
                if (_state.PatternRunning) return;
                _state.PatternRunning = true;
                _patternCts = new CancellationTokenSource();
                var token = _patternCts.Token;

                Task.Run(async () =>
                {
                    var colors = new (byte r, byte g, byte b, byte w, string name)[]
                    {
                        (255, 0, 0, 0, "Red"),
                        (0, 255, 0, 0, "Green"),
                        (0, 0, 255, 0, "Blue"),
                        (0, 0, 0, 255, "White")
                    };

                    int index = 0;
                    while (!token.IsCancellationRequested)
                    {
                        var c = colors[index % colors.Length];
                        SetPixelColor(0, gain: 255, c.r, c.g, c.b, c.w);
                        index++;

                        try
                        {
                            await Task.Delay(1000, token);
                        }
                        catch (TaskCanceledException)
                        {
                            break;
                        }
                    }
                }, token);
            }
        }

        private static void StopPattern()
        {
            lock (_patternLock)
            {
                if (!_state.PatternRunning) return;
                _state.PatternRunning = false;
                _patternCts?.Cancel();
                _patternCts?.Dispose();
                _patternCts = null;
            }
        }

        static void SetPixelColor(int pixelIndex, byte gain, byte r, byte g, byte b, byte w, int startChannel = 1)
        {
            int baseChannel = startChannel + (pixelIndex * 5);
            EnttecOpenDmx.SetDmxValue(baseChannel, gain);
            EnttecOpenDmx.SetDmxValue(baseChannel + 1, r);
            EnttecOpenDmx.SetDmxValue(baseChannel + 2, g);
            EnttecOpenDmx.SetDmxValue(baseChannel + 3, b);
            EnttecOpenDmx.SetDmxValue(baseChannel + 4, w);
        }
    }
}

