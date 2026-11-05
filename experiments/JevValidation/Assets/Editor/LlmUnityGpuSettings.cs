using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;

namespace JevValidation
{
    public enum LlmUnityBackend { CPU, GPU }

    public static class LlmUnityGpuSettings
    {
        public static string BuildCommand(string modelPath, LlmUnityBackend backend, int gpuLayers, int tokenCount = 256)
        {
            if ((backend != LlmUnityBackend.CPU && backend != LlmUnityBackend.GPU) ||
                (backend == LlmUnityBackend.GPU && (gpuLayers < 1 || gpuLayers > 99)))
                throw new InvalidDataException("GPU offload層数は1から99を指定してください。99は全層を要求します。");
            if (tokenCount != 256 && tokenCount != 512) throw new InvalidDataException("contextは256または512tokenを指定してください。");
            var layers = backend == LlmUnityBackend.GPU ? gpuLayers : 0;
            return "-m \"" + modelPath + "\" --embedding --pooling none -c " + tokenCount + " -b " + tokenCount + " -ub " + tokenCount + " -np 1 -ngl " + layers + " -t 4" +
                (backend == LlmUnityBackend.GPU ? " --log-verbosity 3" : "");
        }
    }

    public sealed class GpuOffloadEvidence
    {
        private int _offloadedLayers;
        private int _totalLayers;
        private float _modelBufferMib;
        public bool Verified { get { lock (_lock) return _offloadedLayers > 0 && _offloadedLayers <= _totalLayers && _modelBufferMib > 0; } }
        private readonly object _lock = new object();
        private readonly List<string> _lines = new List<string>();
        private static readonly Regex OffloadPattern = new Regex(@"offloaded (\d+)/(\d+) layers to GPU", RegexOptions.CultureInvariant);
        private static readonly Regex BufferPattern = new Regex(@"CUDA\d+.*model buffer size\s*=\s*([\d.]+) MiB", RegexOptions.CultureInvariant);
        public string[] Lines { get { lock (_lock) return _lines.ToArray(); } }
        public Snapshot Read() { lock (_lock) return new Snapshot(_offloadedLayers, _totalLayers, _modelBufferMib, _lines.ToArray()); }

        public void Capture(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            lock (_lock)
            foreach (var source in message.Split('\n'))
            {
                var match = OffloadPattern.Match(source);
                var bufferMatch = BufferPattern.Match(source);
                if (bufferMatch.Success && float.TryParse(bufferMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var mib) && mib > 0 && mib < 65536)
                    _modelBufferMib = mib;
                var offload = match.Success && int.TryParse(match.Groups[1].Value, out var count) &&
                    int.TryParse(match.Groups[2].Value, out var total) && count >= 0 && count <= total && total <= 512;
                if (offload)
                {
                    _offloadedLayers = int.Parse(match.Groups[1].Value);
                    _totalLayers = int.Parse(match.Groups[2].Value);
                }
                if (!offload && !source.Contains("CUDA") && !source.Contains("Vulkan") && !source.Contains("GPU") && !source.Contains("NVIDIA")) continue;
                var line = source.Trim();
                if (line.Length > 512) line = line.Substring(0, 512);
                if (_lines.Count < 64) _lines.Add(line);
                else if (offload) _lines[_lines.Count - 1] = line;
            }
        }

        public sealed class Snapshot
        {
            public readonly int OffloadedLayers;
            public readonly int TotalLayers;
            public readonly float ModelBufferMib;
            public readonly string[] Lines;
            public Snapshot(int offloaded, int total, float modelBufferMib, string[] lines)
            {
                OffloadedLayers = offloaded;
                TotalLayers = total;
                ModelBufferMib = modelBufferMib;
                Lines = lines;
            }
        }
    }
}
