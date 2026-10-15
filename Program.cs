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

        private static CancellationTokenSource? _randomEffectCts;
        private static readonly object _randomEffectLock = new();

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
                onRandomEffectToggle: OnRandomEffectToggle,
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
                StopRandomEffect();
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
            // If manual color changed, disable active effects/patterns
            if (_state.PatternRunning)
            {
                StopPattern();
            }
            if (_state.RandomEffectRunning)
            {
                StopRandomEffect();
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
                if (_state.RandomEffectRunning)
                {
                    StopRandomEffect();
                }
                StartPattern();
            }
            else
            {
                StopPattern();
                // Restore manual state color
                SetPixelColor(0, _state.Gain, _state.R, _state.G, _state.B, _state.W);
            }
        }

        private static void OnRandomEffectToggle(bool enabled)
        {
            if (enabled)
            {
                if (_state.PatternRunning)
                {
                    StopPattern();
                }
                StartRandomEffect();
            }
            else
            {
                StopRandomEffect();
                // Restore manual state color
                SetPixelColor(0, _state.Gain, _state.R, _state.G, _state.B, _state.W);
            }
        }

        private static void StartRandomEffect()
        {
            lock (_randomEffectLock)
            {
                if (_state.RandomEffectRunning) return;
                _state.RandomEffectRunning = true;
                _randomEffectCts = new CancellationTokenSource();
                var token = _randomEffectCts.Token;

                Task.Run(async () =>
                {
                    var rand = Random.Shared;

                    while (!token.IsCancellationRequested)
                    {
                        // 3 modes: Strobe burst (45%), Random surge/flicker (35%), or Brief blackout dip (20%)
                        int mode = rand.Next(0, 100);

                        if (mode < 45)
                        {
                            // Rapid lightning / strobe burst: 2 to 6 micro-flashes
                            int flashes = rand.Next(2, 7);
                            for (int i = 0; i < flashes && !token.IsCancellationRequested; i++)
                            {
                                byte flashGain = (byte)rand.Next(180, 256);
                                byte flashWhite = (byte)rand.Next(160, 256);
                                SetPixelColor(0, flashGain, _state.R, _state.G, _state.B, flashWhite);
                                await Task.Delay(rand.Next(20, 55), token);

                                // Micro dip between flashes
                                SetPixelColor(0, 0, _state.R, _state.G, _state.B, 0);
                                await Task.Delay(rand.Next(15, 45), token);
                            }
                        }
                        else if (mode < 80)
                        {
                            // Random flicker / pulse across Gain and White
                            int steps = rand.Next(3, 7);
                            for (int s = 0; s < steps && !token.IsCancellationRequested; s++)
                            {
                                byte surgeGain = (byte)rand.Next(60, 256);
                                byte surgeWhite = (byte)rand.Next(30, 256);
                                SetPixelColor(0, surgeGain, _state.R, _state.G, _state.B, surgeWhite);
                                await Task.Delay(rand.Next(40, 110), token);
                            }
                        }
                        else
                        {
                            // Brief dip / blackout pause
                            SetPixelColor(0, 0, _state.R, _state.G, _state.B, 0);
                            await Task.Delay(rand.Next(50, 180), token);
                        }

                        // Randomized delay between bursts
                        int restDelay = rand.Next(80, 750);
                        try
                        {
                            await Task.Delay(restDelay, token);
                        }
                        catch (TaskCanceledException)
                        {
                            break;
                        }
                    }
                }, token);
            }
        }

        private static void StopRandomEffect()
        {
            lock (_randomEffectLock)
            {
                if (!_state.RandomEffectRunning) return;
                _state.RandomEffectRunning = false;
                _randomEffectCts?.Cancel();
                _randomEffectCts?.Dispose();
                _randomEffectCts = null;
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

