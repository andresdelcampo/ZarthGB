using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        private int SampleRate = 192000;    // Minimum 192kHz to get enough frequency resolution -else sounds distorted
        private int NumChannels = 1;
        private WaveOutEvent waveOut1 = new WaveOutEvent();
        private WaveOutEvent waveOut2 = new WaveOutEvent();
        private BufferedWaveProvider waveBuffer1;
        private BufferedWaveProvider waveBuffer2;
        
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
        
        #region Sound1
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
        private byte FrequencyLow1 => memory[0xff13];
        private byte FrequencyHigh1 => (byte)(memory[0xff14] & 7);
        private bool TriggerSound1 => (memory[0xff14] >> 7) != 0;
        private int Frequency1 => (FrequencyHigh1 << 8) | FrequencyLow1;
        private bool Loop1 => (memory[0xff14] & 0x40) == 0;
        
        public void StartSound1()
        {
            if (TriggerSound1)
            {
                ISampleProvider waveSound;
                if (SweepPeriod == 0)
                {
                    // No sweep
                    waveSound = new GBSignalGenerator(SampleRate, NumChannels) { 
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
                    waveSound = new GBSignalGenerator(SampleRate, NumChannels) { 
                            Gain = Volume1, 
                            Frequency = Frequency1,
                            WaveDuty = WaveDuty1,
                            EnvelopeAmplify = EnvelopeAmplify1,
                            EnvelopePeriod = EnvelopePeriod1,
                            SweepPeriod = SweepPeriod,
                            SweepAmplify = SweepAmplify,
                            SweepShift = SweepShift,
                            Type = SignalGeneratorType.Sweep,
                        }
                        .Take(TimeSpan.FromMilliseconds(Length1));
                    Debug.Print($"QUEUE SWEEP Amplify {SweepAmplify}, Period {SweepPeriod}, Shift {SweepShift}");
                }
                
                var buffer = new byte[waveSound.WaveFormat.AverageBytesPerSecond / 4];
                var bytes = waveSound.ToWaveProvider().Read(buffer, 0, buffer.Length);
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
        private byte WaveLength2 => memory[0xff16];
        private int WaveDuty2 => WaveLength2 >> 6;
        private int Length2 => (64 - (WaveLength2 & 0x3F)) * 3;     // Should be 4 but sounds better/faster this way
        private byte Envelope2 => memory[0xff17];
        private double Volume2 => (Envelope2 >> 4) / 15.0;
        private bool EnvelopeAmplify2 => (Envelope2 & 0x8) != 0;
        private int EnvelopePeriod2 => (Envelope2 & 0x7);
        private byte FrequencyLow2 => memory[0xff18];
        private byte FrequencyHigh2 => (byte)(memory[0xff19] & 7);
        private bool TriggerSound2 => (memory[0xff19] >> 7) != 0;
        private int Frequency2 => (FrequencyHigh2 << 8) | FrequencyLow2;
        private bool Loop2 => (memory[0xff19] & 0x40) == 0;

        public void StartSound2()
        {
            if (TriggerSound2)
            {
                var waveSound = new GBSignalGenerator(SampleRate, NumChannels) { 
                        Gain = Volume2, 
                        Frequency = Frequency2, 
                        WaveDuty = WaveDuty2,
                        EnvelopeAmplify = EnvelopeAmplify2,
                        EnvelopePeriod = EnvelopePeriod2,
                    }
                    .Take(TimeSpan.FromMilliseconds(Length2));
                
                Debug.Print($"QUEUE Freq {Frequency2}, Duration {Length2}, Vol {Volume2}, Amplify {EnvelopeAmplify2}, Period {EnvelopePeriod2}, Loop {Loop2}");
                
                var buffer = new byte[waveSound.WaveFormat.AverageBytesPerSecond / 4];
                var bytes = waveSound.ToWaveProvider().Read(buffer, 0, buffer.Length);
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
        
        public Sound(Memory memory)
        {
            this.memory = memory;
            Reset();

            waveBuffer1 = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate,NumChannels));
            waveBuffer2 = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate,NumChannels));
            waveOut1.Init(waveBuffer1);
            waveOut2.Init(waveBuffer2);
        }

        public void Reset()
        {
        }

        public void Play()
        {
            if (Sound1On)
            {
                if (waveOut1.PlaybackState != PlaybackState.Playing)
                    waveOut1.Play();
                SetSound1Off();
            }

            if (Sound2On)
            {
                if (waveOut2.PlaybackState != PlaybackState.Playing)
                    waveOut2.Play();
                SetSound2Off();
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