using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NAudio.Wave;

// References:
// - http://marc.rawer.de/Gameboy/Docs/GBCPUman.pdf probably the best GameBoy CPU/memory manual
// - https://nightshade256.github.io/2021/03/27/gb-sound-emulation.html
// - https://github.com/naudio/NAudio/blob/master/Docs/PlaySineWave.md

namespace ZarthGB
{
    class Sound
    {
        public const int PlayStep = 64;

        private Memory memory;
        private const int SampleRate = 192000;    // Minimum 192kHz to get enough frequency resolution -else sounds distorted
        private const int NumChannels = 1;
        private Stopwatch stopwatch = new Stopwatch();
        private TimeSpan lastTime;
        private MixingWaveProvider32 mixer;
        private WaveOut waveOut = new WaveOut();
        private BufferedWaveProvider waveBuffer;
        private int buffering;
        private const int BufferingRounds = 0;
        
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
        private bool Sound2On => (OnOff & (1 << 1)) != 0;
        private bool Sound3On => (OnOff & (1 << 2)) != 0;
        private bool Sound4On => (OnOff & (1 << 3)) != 0;
        
        #region Sound1
        private GBSignalGenerator signal1;
        private BufferedWaveProvider waveBuffer1;
        private Dictionary<string, byte[]> bufferCache1 = new Dictionary<string, byte[]>();
        private byte Sweep1 => memory[0xff10];
        private int SweepShift => Sweep1 & 0x7; 
        private bool SweepAmplify => (Sweep1 & 0x8) == 0;
        private int SweepPeriod => (Sweep1 >> 4) & 0x7;
        private byte WaveLength1 => memory[0xff11];
        private int WaveDuty1 => WaveLength1 >> 6;
        private int Length1 => (64 - (WaveLength1 & 0x3F)) * 3; // Should be 4 but sounds better/faster this way
        private int lengthPlayed1 = 0;
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
                if (SweepPeriod == 0)
                {
                    // No sweep
                    signal1 = new GBSignalGenerator(SampleRate, NumChannels)
                    {
                        Channel = GBSignalGenerator.ChannelType.Square,
                        Gain = Volume1,
                        Frequency = Frequency1,
                        WaveDuty = WaveDuty1,
                        EnvelopeAmplify = EnvelopeAmplify1,
                        EnvelopePeriod = EnvelopePeriod1,
                    };
                }
                else
                {
                    // Sweep
                    signal1 = new GBSignalGenerator(SampleRate, NumChannels)
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
                    };
                    Debug.Print($"QUEUE SWEEP Amplify {SweepAmplify}, Period {SweepPeriod}, Shift {SweepShift}");
                }
                
                SetSound1On();
            }
        }

        private void SetSound1On()
        {
            lengthPlayed1 = 0;
            OnOff = (byte) (OnOff | 0x01);
        }

        private void SetSound1Off()
        {
            OnOff = (byte) (OnOff & 0xFE);      // 11111110
        }

        #endregion

        #region Sound2
        private GBSignalGenerator signal2;
        private BufferedWaveProvider waveBuffer2;
        private Dictionary<string, byte[]> bufferCache2 = new Dictionary<string, byte[]>();
        private byte WaveLength2 => memory[0xff16];
        private int WaveDuty2 => WaveLength2 >> 6;
        private int Length2 => (64 - (WaveLength2 & 0x3F)) * 3;     // Should be 4 but sounds better/faster this way
        private int lengthPlayed2 = 0;
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
                signal2 = new GBSignalGenerator(SampleRate, NumChannels)
                {
                    Channel = GBSignalGenerator.ChannelType.Square,
                    Gain = Volume2,
                    Frequency = Frequency2,
                    WaveDuty = WaveDuty2,
                    EnvelopeAmplify = EnvelopeAmplify2,
                    EnvelopePeriod = EnvelopePeriod2,
                };

                SetSound2On();
            }
        }

        private void SetSound2On()
        {
            lengthPlayed2 = 0;
            OnOff = (byte) (OnOff | 0x02);
        }

        private void SetSound2Off()
        {
            OnOff = (byte) (OnOff & 0xFD);      // 11111101
        }

        #endregion

        #region Sound3
        private GBSignalGenerator signal3;
        private BufferedWaveProvider waveBuffer3;
        private Dictionary<string, byte[]> bufferCache3 = new Dictionary<string, byte[]>();
        private bool SoundOn3 => (memory[0xff1a] & 0x7) != 0;
        private int Length3 => (int)(((double)(256 - memory[0xff1b]) * 3.0) / 4.0);
        private int lengthPlayed3 = 0;
        private int OutputLevel3 => (memory[0xff1c] & 0x60) >> 5;
        private bool TriggerSound3 => (memory[0xff1e] >> 7) != 0;
        private int Frequency3 => (memory[0xff1e] & 7) << 8 | memory[0xff1d];        
        private bool Loop3 => (memory[0xff1e] & 0x40) == 0;
        private int WaveRamStart = 0xff30;
        private int[] Samples => new[]
        {
            memory[WaveRamStart] >> 4, memory[WaveRamStart] & 0xF,
            memory[WaveRamStart + 1] >> 4, memory[WaveRamStart + 1] & 0xF,
            memory[WaveRamStart + 2] >> 4, memory[WaveRamStart + 2] & 0xF,
            memory[WaveRamStart + 3] >> 4, memory[WaveRamStart + 3] & 0xF,
            memory[WaveRamStart + 4] >> 4, memory[WaveRamStart + 4] & 0xF,
            memory[WaveRamStart + 5] >> 4, memory[WaveRamStart + 5] & 0xF,
            memory[WaveRamStart + 6] >> 4, memory[WaveRamStart + 6] & 0xF,
            memory[WaveRamStart + 7] >> 4, memory[WaveRamStart + 7] & 0xF,
            memory[WaveRamStart + 8] >> 4, memory[WaveRamStart + 8] & 0xF,
            memory[WaveRamStart + 9] >> 4, memory[WaveRamStart + 9] & 0xF,
            memory[WaveRamStart + 10] >> 4, memory[WaveRamStart + 10] & 0xF,
            memory[WaveRamStart + 11] >> 4, memory[WaveRamStart + 11] & 0xF,
            memory[WaveRamStart + 12] >> 4, memory[WaveRamStart + 12] & 0xF,
            memory[WaveRamStart + 13] >> 4, memory[WaveRamStart + 13] & 0xF,
            memory[WaveRamStart + 14] >> 4, memory[WaveRamStart + 14] & 0xF,
            memory[WaveRamStart + 15] >> 4, memory[WaveRamStart + 15] & 0xF,
        };
        
        public void StartSound3()
        {
            if (TriggerSound3)
            {
                signal3 = new GBSignalGenerator(SampleRate, NumChannels)
                {
                    Channel = GBSignalGenerator.ChannelType.Samples,
                    Frequency = Frequency3,
                    Samples = Samples,
                    OutputShift = Pattern2Shift(OutputLevel3)
                };

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
            lengthPlayed3 = 0;
            OnOff = (byte) (OnOff | 0x04);
        }

        private void SetSound3Off()
        {
            OnOff = (byte) (OnOff & 0xFB);      // 11111011
        }
        
        #endregion

        #region Sound4

        private GBSignalGenerator signal4;
        private BufferedWaveProvider waveBuffer4;
        private Dictionary<string, byte[]> bufferCache4 = new Dictionary<string, byte[]>();
        private int Length4 => (64 - (memory[0xff20] & 0x3F)) * 3;     // Should be 4 but sounds better/faster this way
        private int lengthPlayed4 = 0;
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
                signal4 = new GBSignalGenerator(SampleRate, NumChannels)
                {
                    Channel = GBSignalGenerator.ChannelType.Noise,
                    Gain = Volume4,
                    CounterShift = CounterShift,
                    CounterWidthMode = CounterWidthMode,
                    CounterDivisor = CounterDividingRatio,
                    EnvelopeAmplify = EnvelopeAmplify4,
                    EnvelopePeriod = EnvelopePeriod4,
                };

                SetSound4On();
            }
        }
        
        private void SetSound4On()
        {
            lengthPlayed4 = 0;
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

            waveBuffer = new BufferedWaveProvider(new WaveFormat(SampleRate, 2));

            waveBuffer1 = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate,NumChannels));
            waveBuffer2 = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate,NumChannels));
            waveBuffer3 = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate,NumChannels));
            waveBuffer4 = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate,NumChannels));
            //waveBuffer1.BufferDuration = TimeSpan.FromMilliseconds(PlayStep*32);
            //waveBuffer2.BufferDuration = TimeSpan.FromMilliseconds(PlayStep*32);
            //waveBuffer3.BufferDuration = TimeSpan.FromMilliseconds(PlayStep*32);
            //waveBuffer4.BufferDuration = TimeSpan.FromMilliseconds(PlayStep*32);
            waveBuffer1.DiscardOnBufferOverflow = true;
            waveBuffer2.DiscardOnBufferOverflow = true;
            waveBuffer3.DiscardOnBufferOverflow = true;
            waveBuffer4.DiscardOnBufferOverflow = true;
            waveBuffer1.ReadFully = false;
            waveBuffer2.ReadFully = false;
            waveBuffer3.ReadFully = false;
            waveBuffer4.ReadFully = false;
            mixer = new MixingWaveProvider32(new [] { waveBuffer1, waveBuffer2, waveBuffer3, waveBuffer4 } );
            //mixer = new MixingWaveProvider32(new [] { waveBuffer1, waveBuffer2} );
            //mixer = new MixingWaveProvider32(new [] { waveBuffer1 } );
            waveOut.DesiredLatency = PlayStep;
            //waveOut.NumberOfBuffers = 4;
            waveOut.Init(mixer);
            
            //waveOut.Init(waveBuffer);
            buffering = BufferingRounds;
        }

        public void Reset()
        {
            stopwatch.Restart();
        }

        public void Play()
        {
            TimeSpan elapsed = stopwatch.Elapsed - lastTime;
            lastTime = stopwatch.Elapsed;
            int playStep = elapsed.Milliseconds;
            
            Debug.Print($"Sound: {playStep} / {PlayStep}, {waveOut.PlaybackState}");
            Debug.Print($"Buffer1: {waveBuffer1.BufferedDuration.Milliseconds}, Buffer2: {waveBuffer2.BufferedDuration.Milliseconds}, Buffer3: {waveBuffer3.BufferedDuration.Milliseconds}, Buffer4: {waveBuffer4.BufferedDuration.Milliseconds}, ");
 
            if (signal1 != null)
            {
                var playLength = (Loop1 || (lengthPlayed1 + playStep) <= Length1) ? playStep : Length1 - lengthPlayed1;
                if (playLength > 0)
                {
                    byte[] buffer;
                    int bytes;
                    string key = $"{playLength}-{Frequency1}-{WaveDuty1}-{EnvelopeAmplify1}-{EnvelopePeriod1}-{SweepAmplify}-{SweepPeriod}-{SweepShift}-{Volume1}";

                    if (bufferCache1.ContainsKey(key))
                    {
                        buffer = bufferCache1[key];
                        bytes = buffer.Length;
                    }
                    else
                    {
                        buffer = new byte[waveBuffer1.WaveFormat.AverageBytesPerSecond * playLength / 1000];
                        var sample = signal1.Take(TimeSpan.FromMilliseconds(playLength));
                        bytes = sample.ToWaveProvider().Read(buffer, 0, buffer.Length);
                        bufferCache1[key] = buffer;
                    }
                    waveBuffer1.AddSamples(buffer, 0, bytes);
                    lengthPlayed1 += playLength;
                }
                else
                    SetSound1Off();
            }
            
            if (signal2 != null)
            {
                var playLength = (Loop2 || (lengthPlayed2 + playStep) <= Length2) ? playStep : Length2 - lengthPlayed2;
                if (playLength > 0)
                {
                    byte[] buffer;
                    int bytes;
                    string key = $"{playLength}-{Frequency2}-{WaveDuty2}-{EnvelopeAmplify2}-{EnvelopePeriod2}-{Volume2}";

                    if (bufferCache2.ContainsKey(key))
                    {
                        buffer = bufferCache2[key];
                        bytes = buffer.Length;
                    }
                    else
                    {
                        buffer = new byte[waveBuffer2.WaveFormat.AverageBytesPerSecond * playLength / 1000];
                        var sample = signal2.Take(TimeSpan.FromMilliseconds(playLength));
                        bytes = sample.ToWaveProvider().Read(buffer, 0, buffer.Length);
                        bufferCache2[key] = buffer;
                    }
                    waveBuffer2.AddSamples(buffer, 0, bytes);
                    lengthPlayed2 += playLength;
                }
                else
                    SetSound2Off();
            }
 
            if (signal3 != null)
            {
                var playLength = (Loop3 || (lengthPlayed3 + playStep) <= Length3) ? playStep : Length3 - lengthPlayed3;
                if (playLength > 0)
                {
                    byte[] buffer;
                    int bytes;
                    string key = $"{playLength}-{Frequency3}-{OutputLevel3}-{Samples.Sum()}";
    
                    if (bufferCache3.ContainsKey(key))
                    {
                        buffer = bufferCache3[key];
                        bytes = buffer.Length;
                    }
                    else
                    {
                        buffer = new byte[waveBuffer3.WaveFormat.AverageBytesPerSecond * playLength / 1000];
                        var sample = signal3.Take(TimeSpan.FromMilliseconds(playLength));
                        bytes = sample.ToWaveProvider().Read(buffer, 0, buffer.Length);
                        bufferCache3[key] = buffer;
                    }
                    waveBuffer3.AddSamples(buffer, 0, bytes);
                    lengthPlayed3 += playLength;
                }
                else
                    SetSound3Off();
            }
                 
            if (signal4 != null)
            {
                var playLength = (Loop4 || (lengthPlayed4 + playStep) <= Length4) ? playStep : Length4 - lengthPlayed4;
                if (playLength > 0)
                {
                    byte[] buffer;
                    int bytes;
                    string key = $"{playLength}-{EnvelopeAmplify4}-{EnvelopePeriod4}-{Volume4}-{CounterShift}-{CounterWidthMode}-{CounterDividingRatio}";
            
                    if (bufferCache4.ContainsKey(key))
                    {
                        buffer = bufferCache4[key];
                        bytes = buffer.Length;
                    }
                    else
                    {
                        buffer = new byte[waveBuffer4.WaveFormat.AverageBytesPerSecond * playLength / 1000];
                        var sample = signal4.Take(TimeSpan.FromMilliseconds(playLength));
                        bytes = sample.ToWaveProvider().Read(buffer, 0, buffer.Length);
                        bufferCache4[key] = buffer;
                    }
                    waveBuffer4.AddSamples(buffer, 0, bytes);
                    lengthPlayed4 += playLength;
                }
                else
                    SetSound4Off();
            }
    
            //Debug.Print($"Buffer1: {waveBuffer1.BufferedDuration.Milliseconds}, Buffer2: {waveBuffer2.BufferedDuration.Milliseconds}, Buffer3: {waveBuffer3.BufferedDuration.Milliseconds}, Buffer4: {waveBuffer4.BufferedDuration.Milliseconds}, ");

            if (waveOut.PlaybackState != PlaybackState.Playing)
            {
                if (Sound1On || Sound2On || Sound3On || Sound4On)
                {
                    if (buffering > 0)
                        buffering--; // Give time to the buffers to build up
                    else
                        waveOut.Play();
                }
                else
                    buffering = BufferingRounds;
            }
            
            if (!Sound1On) signal1 = null;
            if (!Sound2On) signal2 = null;
            if (!Sound3On || !SoundOn3) signal3 = null;
            if (!Sound4On) signal4 = null;

            /*
            if (waveOut.PlaybackState == PlaybackState.Stopped)
            {
                signal1 = null;
                signal2 = null;
                signal3 = null;
                signal4 = null;
                waveBuffer1.ClearBuffer();
                waveBuffer2.ClearBuffer();
                waveBuffer3.ClearBuffer();
                waveBuffer4.ClearBuffer();
            }*/
        }
    }
}