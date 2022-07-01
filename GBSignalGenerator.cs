using System;
using System.Diagnostics;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ZarthGB
{
    public class GBSignalGenerator : ISampleProvider
    {
        private int nSample;
        private double phi;
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
        
        public WaveFormat WaveFormat { get; }

        public double Gain
        {
            get => gain;
            set
            {
                gain = value;
                // Convert range from [-1, 1] to [0 to 15]
                //currentVolume = (int) (15 * (gain + 1) / 2);
                currentVolume = (int) (15 * gain);
            }
        }

        public bool[] PhaseReverse { get; }
        public SignalGeneratorType Type { get; set; }
        public bool EnvelopeAmplify { get; set; }

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
                double clockCorrection = (double)WaveFormat.SampleRate / (double)(4.19*1024*1024);  // 4.19 MHz 
                frequencyTimerStart = (int) (frequencyTimerStart * clockCorrection);    

                //Debug.Print($"correction {clockCorrection}, frequencyTimerStart {frequencyTimerStart}");
                frequencyTimer = 0;
                waveDutyPosition = 0;
            }
        }
        public double FrequencyEnd { get; set; }
        public int WaveDuty { get; set; }
        public double SweepLengthSecs { get; set; }
        
        public GBSignalGenerator(int sampleRate, int channels)
        {
            phi = 0.0;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
            Type = SignalGeneratorType.Square;
            Gain = 1.0;
            PhaseReverse = new bool[1];
            this.SweepLengthSecs = 2.0;
        }
        
        public int Read(float[] buffer, int offset, int count)
        {
            int num1 = offset;
            for (int index1 = 0; index1 < count / WaveFormat.Channels; ++index1)
            {
                double num2;
                switch (Type)
                { 
                    case SignalGeneratorType.Sweep:
                        /*phi += 2.0 * Math.PI * Math.Exp(FrequencyLog + (double) nSample * (FrequencyEndLog - FrequencyLog) / (SweepLengthSecs * (double) WaveFormat.SampleRate)) / (double) WaveFormat.SampleRate;
                        num2 = Gain * Math.Sin(phi);
                        ++nSample;
                        if ((double) nSample > SweepLengthSecs * (double) WaveFormat.SampleRate)
                        {
                            nSample = 0;
                            phi = 0.0;
                            break;
                        }*/

                        ApplyWave();
                        ApplyEnvelope();

                        num2 = (WaveDutyTable[WaveDuty][waveDutyPosition] == '1') ? gain : -gain;
                        nSample++;

                        break;
                    case SignalGeneratorType.Square:
                        //num2 = (double) nSample * (2.0 * Frequency / (double) WaveFormat.SampleRate) % 2.0 - 1.0 >= 0.0 ? Gain : -Gain;
                        ApplyWave();
                        ApplyEnvelope();

                        num2 = (WaveDutyTable[WaveDuty][waveDutyPosition] == '1') ? gain : -gain;
                        nSample++;
                        break;
                    default:
                        num2 = 0.0;
                        break;
                }
                
                for (int index2 = 0; index2 < WaveFormat.Channels; ++index2)
                  buffer[num1++] = !PhaseReverse[index2] ? (float) num2 : (float) -num2;
            }
            return count;
        }

        private void ApplyWave()
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
    }
}