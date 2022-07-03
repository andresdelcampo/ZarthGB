using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

// References:
// - http://marc.rawer.de/Gameboy/Docs/GBCPUman.pdf probably the best GameBoy CPU/memory manual
// - https://github.com/naudio/NAudio/blob/master/Docs/PlaySineWave.md
// - https://nightshade256.github.io/2021/03/27/gb-sound-emulation.html

namespace ZarthGB
{
    class Sound
    {
        private Memory memory;
        private const int SampleRate = 192000;    // Minimum 192kHz to get enough frequency resolution -else sounds distorted
        private const int NumChannels = 1;
        private Stopwatch stopwatch = new Stopwatch();
        private MixingWaveProvider32 mixer;
        private WaveOut waveOut = new WaveOut();
        
        private byte ChannelControl => memory[0xff24];
        private int RightVolume => (ChannelControl >> 4) & 7;
        private int LeftVolume => ChannelControl & 7;
        private bool RightOn => (ChannelControl & 0x80) != 0;
        private bool LeftOn => (ChannelControl & 0x08) != 0;
        
        private byte Output => memory[0xff25];

        private byte OnOff 
        {
            get => memory[0xff26];
            set => memory[0xff26] = value;
        }
        private bool Sound1On => (OnOff & 1) != 0;
        private bool Sound2On => (OnOff & 1 << 1) != 0;
        private bool Sound3On => (OnOff & 1 << 2) != 0;
        private bool Sound4On => (OnOff & 1 << 3) != 0;
        
        #region Sound1
        private BufferedWaveProvider waveBuffer1;
        private Dictionary<string, byte[]> bufferCache1 = new Dictionary<string, byte[]>();
        private byte Sweep1 => memory[0xff10];
        private int SweepShift => Sweep1 & 0x7; 
        private bool SweepAmplify => (Sweep1 & 0x8) == 0;
        private int SweepPeriod => (Sweep1 >> 4) & 0x7;
        private byte WaveLength1 => memory[0xff11];
        private int WaveDuty1 => WaveLength1 >> 6;
        private int Length1 => (64 - (WaveLength1 & 0x3F)) * 3; // Should be 4 but sounds better/faster this way
        private byte Envelope1 => memory[0xff12];
        private double Volume1 => (Envelope1 >> 4) / 15.0;
        private bool EnvelopeAmplify1 => (Envelope1 & 0x8) != 0;
        private int EnvelopePeriod1 => (Envelope1 & 0x7);
        private bool TriggerSound1 => (memory[0xff14] >> 7) != 0;
        private int Frequency1 => ((memory[0xff14] & 7) << 8) | memory[0xff13];
        private bool Loop1 => (memory[0xff14] & 0x40) == 0;
        
        public void StartSound1()
        {
            if (TriggerSound1)
            {
                byte[] buffer;
                int bytes;
                string key = $"{Length1}-{Frequency1}-{WaveDuty1}-{EnvelopeAmplify1}-{EnvelopePeriod1}-{SweepAmplify}-{SweepPeriod}-{SweepShift}-{Volume1}";

                if (bufferCache1.ContainsKey(key))
                {
                    buffer = bufferCache1[key];
                    bytes = buffer.Length;
                }
                else
                {
                    ISampleProvider waveSound;
                    if (SweepPeriod == 0)
                    {
                        // No sweep
                        waveSound = new GBSignalGenerator(SampleRate, NumChannels)
                            {
                                Channel = GBSignalGenerator.ChannelType.Square,
                                Gain = Volume1,
                                Frequency = Frequency1,
                                WaveDuty = WaveDuty1,
                                EnvelopeAmplify = EnvelopeAmplify1,
                                EnvelopePeriod = EnvelopePeriod1,
                            }
                            .Take(TimeSpan.FromMilliseconds(Length1));
                    }
                    else
                    {
                        // Sweep
                        waveSound = new GBSignalGenerator(SampleRate, NumChannels)
                            {
                                Channel = GBSignalGenerator.ChannelType.Sweep,
                                Gain = Volume1,
                                Frequency = Frequency1,
                                WaveDuty = WaveDuty1,
                                EnvelopeAmplify = EnvelopeAmplify1,
                                EnvelopePeriod = EnvelopePeriod1,
                                SweepPeriod = SweepPeriod,
                                SweepAmplify = SweepAmplify,
                                SweepShift = SweepShift,
                            }
                            .Take(TimeSpan.FromMilliseconds(Length1));
                        Debug.Print($"QUEUE SWEEP Amplify {SweepAmplify}, Period {SweepPeriod}, Shift {SweepShift}");
                    }

                    buffer = new byte[waveSound.WaveFormat.AverageBytesPerSecond * Length1 / 1000];
                    bytes = waveSound.ToWaveProvider().Read(buffer, 0, buffer.Length);
                    bufferCache1[key] = buffer;
                }

                waveBuffer1.AddSamples(buffer, 0, bytes);
                
                SetSound1On();
            }
        }

        private void SetSound1On()
        {
            OnOff = (byte) (OnOff | 0x01);
        }

        private void SetSound1Off()
        {
            OnOff = (byte) (OnOff & 0xFE);      // 11111110
        }

        #endregion

        #region Sound2
        private BufferedWaveProvider waveBuffer2;
        private Dictionary<string, byte[]> bufferCache2 = new Dictionary<string, byte[]>();
        private byte WaveLength2 => memory[0xff16];
        private int WaveDuty2 => WaveLength2 >> 6;
        private int Length2 => (64 - (WaveLength2 & 0x3F)) * 3;     // Should be 4 but sounds better/faster this way
        private byte Envelope2 => memory[0xff17];
        private double Volume2 => (Envelope2 >> 4) / 15.0;
        private bool EnvelopeAmplify2 => (Envelope2 & 0x8) != 0;
        private int EnvelopePeriod2 => (Envelope2 & 0x7);
        private bool TriggerSound2 => (memory[0xff19] >> 7) != 0;
        private int Frequency2 => ((memory[0xff19] & 7) << 8) | memory[0xff18];
        private bool Loop2 => (memory[0xff19] & 0x40) == 0;

        public void StartSound2()
        {
            if (TriggerSound2)
            {
                byte[] buffer;
                int bytes;
                string key = $"{Length2}-{Frequency2}-{WaveDuty2}-{EnvelopeAmplify2}-{EnvelopePeriod2}-{Volume2}";

                if (bufferCache2.ContainsKey(key))
                {
                    buffer = bufferCache2[key];
                    bytes = buffer.Length;
                }
                else
                {
                    var waveSound = new GBSignalGenerator(SampleRate, NumChannels) { 
                            Channel = GBSignalGenerator.ChannelType.Square,
                            Gain = Volume2, 
                            Frequency = Frequency2, 
                            WaveDuty = WaveDuty2,
                            EnvelopeAmplify = EnvelopeAmplify2,
                            EnvelopePeriod = EnvelopePeriod2,
                        }
                        .Take(TimeSpan.FromMilliseconds(Length2));
                
                    //Debug.Print($"QUEUE SQUARE Freq {Frequency2}, Duration {Length2}, Vol {Volume2}, Amplify {EnvelopeAmplify2}, Period {EnvelopePeriod2}, Loop {Loop2}");
                
                    buffer = new byte[waveSound.WaveFormat.AverageBytesPerSecond * Length2 / 1000];
                    bytes = waveSound.ToWaveProvider().Read(buffer, 0, buffer.Length);
                    bufferCache2[key] = buffer;
                }
                
                waveBuffer2.AddSamples(buffer, 0, bytes);
                
                SetSound2On();
            }
        }

        private void SetSound2On()
        {
            OnOff = (byte) (OnOff | 0x02);
        }

        private void SetSound2Off()
        {
            OnOff = (byte) (OnOff & 0xFD);      // 11111101
        }

        #endregion

        #region Sound3
        private BufferedWaveProvider waveBuffer3;
        private Dictionary<string, byte[]> bufferCache3 = new Dictionary<string, byte[]>();
        private bool SoundOn3 => (memory[0xff1a] & 0x7) != 0;
        private int Length3 => (int)(((double)(256 - memory[0xff1b]) * 3.0) / 4.0);
        private int OutputLevel3 => (memory[0xff1c] & 0x60) >> 5;
        private bool TriggerSound3 => (memory[0xff1e] >> 7) != 0;
        private int Frequency3 => (memory[0xff1e] & 7) << 8 | memory[0xff1d];        
        private bool Loop3 => (memory[0xff1e] & 0x40) == 0;
        private int WaveRamStart = 0xff30;
        
        public void StartSound3()
        {
            if (TriggerSound3)
            {
                byte[] buffer;
                int bytes;

                int[] samples = 
                {
                    memory[WaveRamStart] >> 4,
                    memory[WaveRamStart] & 0xF,
                    memory[WaveRamStart + 1] >> 4,
                    memory[WaveRamStart + 1] & 0xF,
                    memory[WaveRamStart + 2] >> 4,
                    memory[WaveRamStart + 2] & 0xF,
                    memory[WaveRamStart + 3] >> 4,
                    memory[WaveRamStart + 3] & 0xF,
                    memory[WaveRamStart + 4] >> 4,
                    memory[WaveRamStart + 4] & 0xF,
                    memory[WaveRamStart + 5] >> 4,
                    memory[WaveRamStart + 5] & 0xF,
                    memory[WaveRamStart + 6] >> 4,
                    memory[WaveRamStart + 6] & 0xF,
                    memory[WaveRamStart + 7] >> 4,
                    memory[WaveRamStart + 7] & 0xF,
                    memory[WaveRamStart + 8] >> 4,
                    memory[WaveRamStart + 8] & 0xF,
                    memory[WaveRamStart + 9] >> 4,
                    memory[WaveRamStart + 9] & 0xF,
                    memory[WaveRamStart + 10] >> 4,
                    memory[WaveRamStart + 10] & 0xF,
                    memory[WaveRamStart + 11] >> 4,
                    memory[WaveRamStart + 11] & 0xF,
                    memory[WaveRamStart + 12] >> 4,
                    memory[WaveRamStart + 12] & 0xF,
                    memory[WaveRamStart + 13] >> 4,
                    memory[WaveRamStart + 13] & 0xF,
                    memory[WaveRamStart + 14] >> 4,
                    memory[WaveRamStart + 14] & 0xF,
                    memory[WaveRamStart + 15] >> 4,
                    memory[WaveRamStart + 15] & 0xF,
                };

                string key = $"{Length3}-{Frequency3}-{OutputLevel3}-{samples.Sum()}";

                if (bufferCache3.ContainsKey(key))
                {
                    buffer = bufferCache3[key];
                    bytes = buffer.Length;
                }
                else
                {
                    var waveSound = new GBSignalGenerator(SampleRate, NumChannels)
                        {
                            Channel = GBSignalGenerator.ChannelType.Samples,
                            Frequency = Frequency3,
                            Samples = samples,
                            OutputShift = Pattern2Shift(OutputLevel3)
                        }
                        .Take(TimeSpan.FromMilliseconds(Length3));

                    Debug.Print(
                        $"{stopwatch.ElapsedMilliseconds} QUEUE SAMPLE Freq {Frequency3}, Duration {Length3}, VolShift {Pattern2Shift(OutputLevel3)}, Loop {Loop3}");

                    buffer = new byte[waveSound.WaveFormat.AverageBytesPerSecond * Length3 / 1000];
                    bytes = waveSound.ToWaveProvider().Read(buffer, 0, buffer.Length);
                    bufferCache3[key] = buffer;
                }

                waveBuffer3.AddSamples(buffer, 0, bytes);
                
                SetSound3On();
            }
        }

        private int Pattern2Shift(int outputLevel)
        {
            switch (outputLevel)
            {
                case 0: return 4;
                case 1: return 0;
                case 2: return 1;
                case 3: return 2;
                default: throw new Exception("Invalid output level");
            }
        }
        
        private void SetSound3On()
        {
            OnOff = (byte) (OnOff | 0x04);
        }

        private void SetSound3Off()
        {
            OnOff = (byte) (OnOff & 0xFB);      // 11111011
        }
        
        #endregion

        #region Sound4
        private BufferedWaveProvider waveBuffer4;
        private Dictionary<string, byte[]> bufferCache4 = new Dictionary<string, byte[]>();
        private int Length4 => (64 - (memory[0xff20] & 0x3F)) * 3;     // Should be 4 but sounds better/faster this way
        private byte Envelope4 => memory[0xff21];
        private double Volume4 => (Envelope4 >> 4) / 15.0;
        private bool EnvelopeAmplify4 => (Envelope4 & 0x8) != 0;
        private int EnvelopePeriod4 => (Envelope4 & 0x7);
        private byte PolynomialCounter => memory[0xff22];
        private int CounterShift => (PolynomialCounter >> 4);
        private bool CounterWidthMode => (PolynomialCounter & 8) != 0;
        private int CounterDividingRatio => (PolynomialCounter & 7);
        private bool TriggerSound4 => (memory[0xff23] >> 7) != 0;
        private bool Loop4 => (memory[0xff23] & 0x40) == 0;

        public void StartSound4()
        {
            if (TriggerSound4)
            {
                byte[] buffer;
                int bytes;
                string key = $"{Length4}-{EnvelopeAmplify4}-{EnvelopePeriod4}-{Volume4}-{CounterShift}-{CounterWidthMode}-{CounterDividingRatio}";
                
                if (bufferCache4.ContainsKey(key))
                {
                    buffer = bufferCache4[key];
                    bytes = buffer.Length;
                }
                else
                {
                    var waveSound = new GBSignalGenerator(SampleRate, NumChannels) { 
                            Channel = GBSignalGenerator.ChannelType.Noise,
                            Gain = Volume4, 
                            CounterShift = CounterShift,
                            CounterWidthMode = CounterWidthMode,
                            CounterDivisor = CounterDividingRatio,
                            EnvelopeAmplify = EnvelopeAmplify4,
                            EnvelopePeriod = EnvelopePeriod4,
                        }
                        .Take(TimeSpan.FromMilliseconds(Length4));
                
                    //Debug.Print($"QUEUE NOISE Freq {CounterFrequency}, Duration {Length4}, Vol {Volume4}, Env {EnvelopeAmplify4}, EnvPeriod {EnvelopePeriod4}, EnvStep {CounterStep}, EnvDiv {Divisor(CounterDividingRatio)}, Loop {Loop4}");
                
                    buffer = new byte[waveSound.WaveFormat.AverageBytesPerSecond * Length4 / 1000];
                    bytes = waveSound.ToWaveProvider().Read(buffer, 0, buffer.Length);
                    bufferCache4[key] = buffer;
                }
                
                waveBuffer4.AddSamples(buffer, 0, bytes);
                
                SetSound4On();
            }
        }
        
        private void SetSound4On()
        {
            OnOff = (byte) (OnOff | 0x08);
        }

        private void SetSound4Off()
        {
            OnOff = (byte) (OnOff & 0xF7);      // 11110111
        }
        
        #endregion
        
        public Sound(Memory memory)
        {
            this.memory = memory;
            Reset();

            waveBuffer1 = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate,NumChannels));
            waveBuffer2 = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate,NumChannels));
            waveBuffer3 = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate,NumChannels));
            waveBuffer4 = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate,NumChannels));
            waveBuffer1.BufferDuration = TimeSpan.FromMilliseconds(1000);
            waveBuffer2.BufferDuration = TimeSpan.FromMilliseconds(1000);
            waveBuffer3.BufferDuration = TimeSpan.FromMilliseconds(1000);
            waveBuffer4.BufferDuration = TimeSpan.FromMilliseconds(1000);
            waveBuffer1.DiscardOnBufferOverflow = true;
            waveBuffer2.DiscardOnBufferOverflow = true;
            waveBuffer3.DiscardOnBufferOverflow = true;
            waveBuffer4.DiscardOnBufferOverflow = true;
            mixer = new MixingWaveProvider32(new [] { waveBuffer1, waveBuffer2, waveBuffer3, waveBuffer4 } );
            //mixer = new MixingWaveProvider32(new [] { waveBuffer4 } );
            waveOut.Init(mixer);
        }

        public void Reset()
        {
            stopwatch.Restart();
        }

        public void Play()
        {
            if (Sound1On || Sound2On || Sound3On  || Sound4On)
                waveOut.Play();
        }
    }
}