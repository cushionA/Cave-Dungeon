using System.IO;
using NUnit.Framework;

namespace JevValidation.Editor.Tests
{
    public sealed class LlmUnityGpuTests
    {
        [Test]
        public void LlmUnityCommand_BackendAndLayerBoundary_PreservesExplicitOffloadIntent()
        {
            var cpu = LlmUnityGpuSettings.BuildCommand("C:/Tiny Jev/model.gguf", LlmUnityBackend.CPU, 99);
            var gpu = LlmUnityGpuSettings.BuildCommand("C:/Tiny Jev/model.gguf", LlmUnityBackend.GPU, 99);
            Assert.That(cpu, Does.Contain("-ngl 0"));
            Assert.That(gpu, Does.Contain("-ngl 99"));
            Assert.That(gpu, Does.Contain("-m \"C:/Tiny Jev/model.gguf\""));
            Assert.That(gpu, Does.Contain("--embedding --pooling none"));
            Assert.That(gpu, Does.Contain("--log-verbosity 3"));
            Assert.That(LlmUnityGpuSettings.BuildCommand("model.gguf", LlmUnityBackend.GPU, 99, 512), Does.Contain("-c 512 -b 512 -ub 512"));
            Assert.Throws<InvalidDataException>(() => LlmUnityGpuSettings.BuildCommand("model.gguf", LlmUnityBackend.GPU, 99, 513));
            Assert.Throws<InvalidDataException>(() => LlmUnityGpuSettings.BuildCommand("model.gguf", LlmUnityBackend.GPU, 0));
            Assert.Throws<InvalidDataException>(() => LlmUnityGpuSettings.BuildCommand("model.gguf", LlmUnityBackend.GPU, 100));
        }

        [Test]
        public void GpuEvidence_ActualOffloadAndBoundedLogs_DistinguishesCpuFallback()
        {
            var evidence = new GpuOffloadEvidence();
            evidence.Capture("CUDA device available; offloading 28 repeating layers");
            Assert.That(evidence.Verified, Is.False);
            evidence.Capture("load_tensors: offloaded 0/29 layers to GPU");
            Assert.That(evidence.Verified, Is.False);
            evidence.Capture("load_tensors: CUDA0 model buffer size = 600.50 MiB");
            for (var index = 0; index < 100; index++) evidence.Capture("CUDA " + new string('a', 600));
            evidence.Capture("load_tensors: offloaded 29/29 layers to GPU");
            Assert.That(evidence.Verified, Is.True);
            Assert.That(evidence.Read().OffloadedLayers, Is.EqualTo(29));
            Assert.That(evidence.Read().TotalLayers, Is.EqualTo(29));
            Assert.That(evidence.Read().ModelBufferMib, Is.EqualTo(600.5f));
            Assert.That(evidence.Lines.Length, Is.LessThanOrEqualTo(64));
            Assert.That(evidence.Lines, Has.Some.Contains("offloaded 29/29"));
            foreach (var line in evidence.Lines) Assert.That(line.Length, Is.LessThanOrEqualTo(512));
        }
    }
}
