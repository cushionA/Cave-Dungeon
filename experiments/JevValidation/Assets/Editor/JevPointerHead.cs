using System;
using System.IO;
using System.Text;

namespace JevValidation
{
    public sealed class JevPointerHead
    {
        public const int HiddenSize = 1024;
        public const int HeadDimension = 256;
        private readonly float _temperature;
        private readonly float[] _queryWeight;
        private readonly float[] _queryBias;
        private readonly float[] _keyWeight;
        private readonly float[] _keyBias;

        private JevPointerHead(BinaryReader reader, float temperature)
        {
            _temperature = temperature;
            _queryWeight = ReadFloats(reader, HeadDimension * HiddenSize);
            _queryBias = ReadFloats(reader, HeadDimension);
            _keyWeight = ReadFloats(reader, HeadDimension * HiddenSize);
            _keyBias = ReadFloats(reader, HeadDimension);
        }

        public static JevPointerHead Load(string path, string expectedSha256)
        {
            GgufInputs.VerifyHash(path, expectedSha256);
            using var stream = File.OpenRead(path);
            if (stream.Length != 20L + 4L * (2L * HiddenSize * HeadDimension + 2L * HeadDimension))
                throw new InvalidDataException("Jev headのファイル長が契約と一致しません。");
            using var reader = new BinaryReader(stream);
            if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "JEVHEAD1" ||
                reader.ReadInt32() != HiddenSize || reader.ReadInt32() != HeadDimension)
                throw new InvalidDataException("JEVHEAD1、hidden_size=1024、head_dim=256が必要です。");
            var temperature = reader.ReadSingle();
            if (!GgufInputs.IsFinite(temperature) || temperature <= 0)
                throw new InvalidDataException("Jev headのtemperatureは正の有限値が必要です。");
            return new JevPointerHead(reader, temperature);
        }

        public float[] Predict(float[][] hidden, PreparedCase item)
        {
            var query = Project(hidden[item.decision_position[0]], _queryWeight, _queryBias);
            var logits = new double[item.option_positions.Length];
            for (var choice = 0; choice < logits.Length; choice++)
            {
                var key = Project(hidden[item.option_positions[choice]], _keyWeight, _keyBias);
                double dot = 0;
                for (var index = 0; index < HeadDimension; index++) dot += (double)query[index] * key[index];
                logits[choice] = dot / (Math.Sqrt(HeadDimension) * _temperature);
                if (double.IsNaN(logits[choice]) || double.IsInfinity(logits[choice]))
                    throw new InvalidDataException("Jev headのlogitが有限値ではありません。");
            }
            double maximum = logits[0];
            for (int index = 1; index < logits.Length; index++) maximum = Math.Max(maximum, logits[index]);
            double sum = 0;
            for (var index = 0; index < logits.Length; index++) sum += logits[index] = Math.Exp(logits[index] - maximum);
            var probabilities = new float[logits.Length];
            for (var index = 0; index < logits.Length; index++) probabilities[index] = (float)(logits[index] / sum);
            PreparedCaseFile.ValidateProbabilities(probabilities, logits.Length);
            return probabilities;
        }

        private static float[] Project(float[] hidden, float[] weight, float[] bias)
        {
            var values = new float[HeadDimension];
            for (var row = 0; row < values.Length; row++)
            {
                double sum = bias[row];
                for (var column = 0; column < HiddenSize; column++) sum += (double)weight[row * HiddenSize + column] * hidden[column];
                values[row] = (float)sum;
            }
            return values;
        }

        private static float[] ReadFloats(BinaryReader reader, int count)
        {
            var values = new float[count];
            for (var index = 0; index < count; index++)
            {
                values[index] = reader.ReadSingle();
                if (!GgufInputs.IsFinite(values[index])) throw new InvalidDataException("Jev headに非有限値があります。");
            }
            return values;
        }
    }
}
