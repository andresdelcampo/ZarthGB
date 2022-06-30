using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

// References:
// - http://marc.rawer.de/Gameboy/Docs/GBCPUman.pdf probably the best GameBoy CPU/memory manual
// - https://github.com/naudio/NAudio/blob/master/Docs/PlaySineWave.md

namespace ZarthGB
{
    class Sound
    {
        private Memory memory;
        private CancellationTokenSource cts = new CancellationTokenSource();
        
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
        private bool Sound2On => (OnOff & 1 << 1) != 0;

        #region Sound1
        private ConcurrentQueue<QueuedSound> queueSound1 = new ConcurrentQueue<QueuedSound>();
        private byte Sweep1 => memory[0xff10];
        private int SweepShifts => Sweep1 & 0x7; 
        private bool SweepIncrease => (Sweep1 & 0x8) == 0;
        private int SweepTime => (Sweep1 >> 4) & 0x7;
        private byte WaveLength1 => memory[0xff11];
        private int WaveDuty1 => WaveLength1 >> 6;
        private int Length1 => (64 - (WaveLength1 & 0x3F)) * 4;
        private byte Envelope1 => memory[0xff12];
        private double Volume1 => (Envelope1 >> 4) / 15.0;
        private byte FrequencyLow1 => memory[0xff13];
        private byte FrequencyHigh1 => (byte)(memory[0xff14] & 7);
        private bool TriggerSound1 => (memory[0xff14] >> 7) != 0;
        private int Frequency1 => (FrequencyHigh1 << 8) | FrequencyLow1;
        private bool Loop1 => (memory[0xff14] & 0x40) == 0;
        
        public void StartSound1()
        {
            if (TriggerSound1)
            {
                double frequency = Hz(Frequency1);
                int duration = Length1;
                double waveDuty = WaveDutyToGain(WaveDuty1);
                double sweepTime = SweepTimeInSeconds();
            
                ISampleProvider waveSound1;
                if (sweepTime == 0)
                {
                    // No sweep
                    waveSound1 = new GBSignalGenerator() { 
                            Gain = Volume1, 
                            Frequency = frequency,
                            Type = SignalGeneratorType.Square}
                        .Take(TimeSpan.FromMilliseconds(duration));
                }
                else
                {
                    // Sweep
                    double frequencyStart = frequency;
                    double frequencyEnd = FrequencyEnd();

                    waveSound1 = new GBSignalGenerator() { 
                            Gain = Volume1, 
                            Frequency = frequencyStart,
                            FrequencyEnd = frequencyEnd,
                            Type = SignalGeneratorType.Sweep,
                            SweepLengthSecs = sweepTime 
                        }
                        .Take(TimeSpan.FromMilliseconds(duration));
                }
                
                queueSound1.Enqueue(new QueuedSound(waveSound1, Loop1));
            }
        }
        
        private void PlaySound1(object cancellationToken)
        {
            var token = (CancellationToken)cancellationToken;

            using (var wo = new WaveOutEvent())
            {
                var waveBuffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100,2));
                wo.Init(waveBuffer);

                do
                {
                    while (queueSound1.IsEmpty)
                        Thread.Sleep(5);

                    QueuedSound queuedSound;
                    if (!queueSound1.TryDequeue(out queuedSound))
                        throw new InvalidOperationException("Failed retrieving music");
                    Debug.Print($"DEQUEUE");
                    bool loop = queuedSound.Loop;

                    var buffer = new byte[queuedSound.Wave.WaveFormat.AverageBytesPerSecond / 4];
                    var bytes = queuedSound.Wave.ToWaveProvider().Read(buffer, 0, buffer.Length);
                    waveBuffer.AddSamples(buffer, 0, bytes);
                    
                    do
                    {
                        if (wo.PlaybackState != PlaybackState.Playing)
                            wo.Play();
                        Thread.Sleep(5);
                    } while (loop && !token.IsCancellationRequested && queueSound1.IsEmpty);

                    if (queueSound1.IsEmpty)
                        SetSound1Off();
                    
                } while (!token.IsCancellationRequested);
            } 

        }

        private void SetSound1Off()
        {
            OnOff = (byte) (OnOff & 0xFE);      // 11111110
        }

        private double SweepTimeInSeconds()
        {
            if (SweepTime > 7) throw new InvalidOperationException("Invalid SweepTime coding");
            switch (SweepTime)
            {
                case 0: return 0;
                default: return ((SweepTime * 1000.0) / 128.0);
            }
        }

        private double FrequencyEnd()
        {
            double frequency = Frequency1;
            for (int i = 0; i < SweepShifts; i++)
                if (SweepIncrease)
                    frequency += frequency / (2 ^ SweepShifts);
                else
                    frequency -= frequency / (2 ^ SweepShifts);
            return Hz(frequency);
        }

        #endregion
        
        #region Sound2
        private ConcurrentQueue<QueuedSound> queueSound2 = new ConcurrentQueue<QueuedSound>();
        private byte WaveLength2 => memory[0xff16];
        private int WaveDuty2 => WaveLength2 >> 6;
        private int Length2 => (64 - (WaveLength2 & 0x3F)) * 4;
        private byte Envelope2 => memory[0xff17];
        private double Volume2 => (Envelope2 >> 4) / 15.0;
        private byte FrequencyLow2 => memory[0xff18];
        private byte FrequencyHigh2 => (byte)(memory[0xff19] & 7);
        private bool TriggerSound2 => (memory[0xff19] >> 7) != 0;
        private int Frequency2 => (FrequencyHigh2 << 8) | FrequencyLow2;
        private bool Loop2 => (memory[0xff19] & 0x40) == 0;

        public void StartSound2()
        {
            if (TriggerSound2)
            {
                double frequency = Hz(Frequency2);
                int duration = Length2;
                
                var waveSound2 = new GBSignalGenerator() { 
                        Gain = Volume2, 
                        Frequency = frequency,//Frequency2, 
                        WaveDuty = WaveDuty2,
                        Type = SignalGeneratorType.Square}
                    .Take(TimeSpan.FromMilliseconds(duration));
                
                Debug.Print($"QUEUE Freq {Frequency2}, Duration {duration}, Wave {WaveDuty2}, Vol {Volume2}, Loop {Loop2}");
                
                queueSound2.Enqueue(new QueuedSound(waveSound2, Loop2));
            }
        }

        private void PlaySound2(object cancellationToken)
        {
            var token = (CancellationToken)cancellationToken;
            
            using (var wo = new WaveOutEvent())
            {
                var waveBuffer2 = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100,2));
                wo.Init(waveBuffer2);

                do
                {
                    while (queueSound2.IsEmpty)
                        Thread.Sleep(5);

                    QueuedSound queuedSound;
                    if (!queueSound2.TryDequeue(out queuedSound))
                        throw new InvalidOperationException("Failed retrieving music");
                    Debug.Print($"DEQUEUE");
                    bool loop2 = queuedSound.Loop;

                    var buffer = new byte[queuedSound.Wave.WaveFormat.AverageBytesPerSecond / 4];
                    var bytes = queuedSound.Wave.ToWaveProvider().Read(buffer, 0, buffer.Length);
                    waveBuffer2.AddSamples(buffer, 0, bytes);
                    
                    do
                    {
                        if (wo.PlaybackState != PlaybackState.Playing)
                            wo.Play();
                        Thread.Sleep(5);
                    } while (loop2 && !token.IsCancellationRequested && queueSound2.IsEmpty);

                    if (queueSound2.IsEmpty)
                        SetSound2Off();
                    
                } while (!token.IsCancellationRequested);
            } 
        }

        private void SetSound2Off()
        {
            OnOff = (byte) (OnOff & 0xFD);      // 11111101
        }

        #endregion
        
        public Sound(Memory memory)
        {
            this.memory = memory;
            Reset();
            ThreadPool.QueueUserWorkItem(PlaySound1, cts.Token);
            ThreadPool.QueueUserWorkItem(PlaySound2, cts.Token);
        }

        public void Reset()
        {
        }

        private static double Hz(double gb)
        {
            return 131072.0 / (2048.0 - gb);
        }

        private static double WaveDutyToGain(int duty)
        {
            switch (duty)
            {
                case 3: return 0.75;
                case 2: return 0.50;
                case 1: return 0.25;
                case 0: return 0.125;
                default: throw new InvalidOperationException("Invalid data on WaveDuty");
            }
        }
    }

    class QueuedSound
    {
        public ISampleProvider Wave { get; set; }
        public bool Loop { get; set; }

        public QueuedSound(ISampleProvider wave, bool loop)
        {
            Wave = wave;
            Loop = loop;
        }
    }
}