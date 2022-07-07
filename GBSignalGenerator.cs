using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ZarthGB
{
    public class GBSignalGenerator : ISampleProvider
    {
        public enum ChannelType
        {
            Sweep,
            Square,
            Samples,
            Noise
        }
        
        private int nSample;
        private string[] WaveDutyTable =
        {
            "00000001",
            "00000011",
            "00001111",
            "11111100"
        };
        private int waveDutyPosition;
        private int frequencyTimerStart;
        private int frequencyTimer;
        private double frequency;
        private int currentVolume;
        private int periodTimer;
        private double gain;
        private int envelopePeriod;
        private int shadowFrequency;
        private int sweepTimer;
        private ushort lfsr;
        private double clockCorrection;

        public WaveFormat WaveFormat { get; }
        public int WaveDuty { get; set; }
        public int SweepPeriod { get; set; }
        public bool SweepAmplify { get; set; }
        public int SweepShift { get; set; }
        public ChannelType Channel { get; set; }
        public bool EnvelopeAmplify { get; set; }
        public int[] Samples = new int[32];
        public int OutputShift { get; set; }
        public int CounterShift { get; set; }
        public bool CounterWidthMode { get; set; }
        public int CounterDivisor { get; set; }

        public double Gain
        {
            get => gain;
            set
            {
                gain = value;
                currentVolume = (int) (15 * gain);
                lfsr = (ushort)currentVolume;
            }
        }

        public int EnvelopePeriod
        {
            get => envelopePeriod;
            set => envelopePeriod = value * 2048;
            // I cannot explain 2048 -I was expecting 64*4 (64 Hz when sampled in 44.1 kHz, so 256 Hz when sampled in 192 kHz)
        }
        
        public double Frequency
        {
            get => frequency;
            set
            {
                frequency = value;
                frequencyTimerStart = (2048 - (int)Frequency) * 4;
                frequencyTimerStart = (int) (frequencyTimerStart * clockCorrection);    
                frequencyTimer = 0;
                waveDutyPosition = 0;
            }
        }
        
        public GBSignalGenerator(int sampleRate, int channels)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
            Gain = 1.0;
            clockCorrection = (double)WaveFormat.SampleRate / (double)(4.19*1024*1024);  // 4.19 MHz 
        }
        
        public int Read(float[] buffer, int offset, int count)
        {
            int bufOffset = offset;
            for (int index1 = 0; index1 < count / WaveFormat.Channels; ++index1)
            {
                double sample;
                switch (Channel)
                { 
                    case ChannelType.Sweep:
                        ApplySquareWave();
                        ApplyEnvelope();
                        ApplySweep();
                        sample = (WaveDutyTable[WaveDuty][waveDutyPosition] == '1') ? gain: -gain;
                        nSample++;
                        break;
                    
                    case ChannelType.Square:
                        ApplySquareWave();
                        ApplyEnvelope();
                        sample = (WaveDutyTable[WaveDuty][waveDutyPosition] == '1') ? gain: -gain;
                        nSample++;
                        break;
                    
                    case ChannelType.Samples:
                        ApplySamples();
                        sample = (((Samples[waveDutyPosition] & 0xF) >> OutputShift) - 7.5) / 7.5;
                        nSample++;
                        break;
                    
                    case ChannelType.Noise:
                        ApplyNoise();
                        ApplyEnvelope();
                        sample =  ((lfsr & 0x01) == 0) ? gain : -gain;
                        nSample++;
                        break;
                    
                    default:
                        sample = 0.0;
                        break;
                }
                
                for (int index2 = 0; index2 < WaveFormat.Channels; ++index2)
                  buffer[bufOffset++] = (float) sample;
            }
            return count;
        }

        private void ApplyNoise()
        {
            if (frequencyTimer == 0)
            {
                frequencyTimer = (CounterDivisor > 0 ? (CounterDivisor << 4) : 8) << CounterShift;
                frequencyTimer = (int) (frequencyTimer * clockCorrection);    

                byte xorResult = (byte)((lfsr & 1) ^ ((lfsr & 2) >> 1));
                lfsr = (ushort)((lfsr >> 1) | (xorResult << 14));

                if (CounterWidthMode)
                {
                    lfsr = (ushort)(lfsr & ~0x40);
                    lfsr = (ushort)(lfsr | (xorResult << 6));
                }
            }
            else 
                frequencyTimer--;
        }

        private void ApplySamples()
        {
            if (frequencyTimer == 0)
            {
                waveDutyPosition = (waveDutyPosition + 1) % Samples.Length;
                frequencyTimer = frequencyTimerStart;
            }
            else
                frequencyTimer--;
        }

        private void ApplySquareWave()
        {
            if (frequencyTimer == 0)
            {
                waveDutyPosition = (waveDutyPosition + 1) % WaveDutyTable[WaveDuty].Length;
                frequencyTimer = frequencyTimerStart;
            }
            else
                frequencyTimer--;
        }

        private void ApplyEnvelope()
        {
            if (envelopePeriod != 0)
            {
                if (periodTimer > 0)
                    periodTimer -= 1;

                if (periodTimer == 0)
                {
                    periodTimer = envelopePeriod;

                    if ((currentVolume < 0xF && EnvelopeAmplify) ||
                        (currentVolume > 0x0 && !EnvelopeAmplify))
                    {
                        int adjustment = EnvelopeAmplify ? 1 : -1;
                        currentVolume += adjustment;
                        gain = (double) currentVolume / 15.0;
                    }
                }
            }
        }

        private void ApplySweep()
        {
            if (sweepTimer > 0) 
                sweepTimer -= 1;

            if (sweepTimer == 0)
            {
                if (SweepPeriod > 0)
                    sweepTimer = SweepPeriod;
                else
                    sweepTimer = 8;

                if (SweepShift > 0 && SweepPeriod > 0) 
                {
                    int newFrequency = UpdateFrequency();

                    if (newFrequency <= 2047 && SweepShift > 0) 
                    {
                        frequency = newFrequency;
                        shadowFrequency = newFrequency;

                        /* for overflow check */
                        UpdateFrequency();
                    }
                }
            }
        }
        
        private int UpdateFrequency()
        {
            int newFrequency = shadowFrequency >> SweepShift;

            if (SweepAmplify)
                newFrequency = shadowFrequency + newFrequency;
            else
                newFrequency = shadowFrequency - newFrequency;
            
            // overflow check
            if (newFrequency > 2047)
                currentVolume = 0;  // It should be stop channel completely

            return newFrequency;
        }
    }
}