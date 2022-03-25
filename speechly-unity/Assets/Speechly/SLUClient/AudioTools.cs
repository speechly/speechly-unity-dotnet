using System;

namespace Speechly.SLUClient {
  public class AudioTools {

    public static void Downsample(in float[] src, ref float[] dest, int sourceIndex = 0, int sourceLength = -1, int destIndex = 0, int destLength = -1) {
      if (sourceLength < 0) sourceLength = src.Length - sourceIndex;
      if (destLength < 0) destLength = dest.Length - destIndex;

      if (destLength > sourceLength) {
        throw new Exception("Can't downsample: destination array can't be longer than source");
      }

      if (destLength == 0) {
        throw new Exception("Can't downsample: destination array can't be zero-length.");
      }

      if (sourceLength == 0) {
        throw new Exception("Can't downsample: source range can't be zero length.");
      }

      if (sourceLength == 1) {
        dest[0] = src[0];
        return;
      }

      float destIndexFraction = 0f;
      float destStep = ((float)destLength - 1) / (sourceLength - 1);
      float sum = 0;
      float totalWeight = 0;
      int sourceEndIndex = sourceIndex + sourceLength;
      for ( ; sourceIndex < sourceEndIndex; sourceIndex++ ) {
        float weight = 0.5f - Math.Abs(destIndexFraction);
        sum += src[sourceIndex] * weight;
        totalWeight += weight;
        destIndexFraction += destStep;
        if (destIndexFraction >= 0.5f) {
          destIndexFraction -= 1f;
          dest[destIndex++] = sum / totalWeight;
          sum = 0;
          totalWeight = 0;
        }
      }
      // Put last value in place
      if (totalWeight > 0) {
        dest[destIndex++] = sum / totalWeight;
      }
    }

    public static float GetEnergy(in float[] samples, int start = 0, int length = -1) {
      if (length < 0) length = samples.Length - start;
      if (length <= 0) return 0f;
      int endIndex = start + length;
      float sumEnergySquared = 0f;
      for ( ; start < endIndex; start++ ) {
        sumEnergySquared += samples[start] * samples[start];
      }
      return (float)Math.Sqrt(sumEnergySquared / length);
    }

    public static float GetAudioPeak(in float[] samples, int start = 0, int length = -1) {
      if (length < 0) length = samples.Length - start;
      if (length <= 0) return 0f;
      int endIndex = start + length;
      float peak = 0f;
      for ( ; start < endIndex; start++ ) {
        if (samples[start] > peak) {
          peak = samples[start];
        }
      }
      return peak;
    }

  }
}