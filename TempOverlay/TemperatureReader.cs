using System;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;

namespace TempOverlay
{
    internal sealed class TemperatureReader : IDisposable
    {
        private const string CpuPreferredSensorName = "Core (Tctl/Tdie)";
        private const string GpuPreferredSensorName = "GPU Core";
        private const float MinValidTempC = 1f;
        private const float MaxValidTempC = 130f;
        private readonly Computer _computer;

        public TemperatureReader()
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMotherboardEnabled = false,
                IsMemoryEnabled = false,
                IsNetworkEnabled = false,
                IsStorageEnabled = false,
                IsBatteryEnabled = false,
                IsControllerEnabled = false,
                IsPsuEnabled = false
            };

            _computer.Open();
        }

        public TemperatureSnapshot Read()
        {
            try
            {
                var cpuAggregate = new TempAggregate(CpuPreferredSensorName);
                var gpuCandidates = new List<GpuCandidate>();

                foreach (var hardware in _computer.Hardware)
                {
                    hardware.Update();
                    ReadHardwareTemps(hardware, ref cpuAggregate, gpuCandidates);

                    foreach (var sub in hardware.SubHardware)
                    {
                        sub.Update();
                        ReadHardwareTemps(sub, ref cpuAggregate, gpuCandidates);
                    }
                }

                var cpuTemp = cpuAggregate.GetAverage();
                var gpuTemp = GetActiveGpuTemp(gpuCandidates);

                if (cpuTemp.HasValue || gpuTemp.HasValue)
                {
                    return new TemperatureSnapshot(cpuTemp, gpuTemp, null, "LibreHardwareMonitorLib");
                }

                return new TemperatureSnapshot(null, null, "No CPU/GPU temperature sensors found.", "LibreHardwareMonitorLib");
            }
            catch (Exception ex)
            {
                return new TemperatureSnapshot(null, null, ex.Message, "LibreHardwareMonitorLib");
            }
        }

        public void Dispose()
        {
            _computer.Close();
        }

        private static void ReadHardwareTemps(IHardware hardware, ref TempAggregate cpuAggregate, List<GpuCandidate> gpuCandidates)
        {
            if (hardware.HardwareType == HardwareType.Cpu)
            {
                foreach (var sensor in hardware.Sensors)
                {
                    if (sensor.SensorType != SensorType.Temperature || !sensor.Value.HasValue)
                    {
                        continue;
                    }

                    var value = sensor.Value.Value;
                    if (!IsPlausibleTemp(value))
                    {
                        continue;
                    }

                    var isPackageSensor = (sensor.Name ?? string.Empty).IndexOf("package", StringComparison.OrdinalIgnoreCase) >= 0;
                    cpuAggregate.Add(value, isPackageSensor, sensor.Name);
                }

                return;
            }

            if (hardware.HardwareType != HardwareType.GpuNvidia &&
                hardware.HardwareType != HardwareType.GpuAmd &&
                hardware.HardwareType != HardwareType.GpuIntel)
            {
                return;
            }

            var gpuAggregate = new TempAggregate(GpuPreferredSensorName);
            var maxLoad = 0f;

            foreach (var sensor in hardware.Sensors)
            {
                if (!sensor.Value.HasValue)
                {
                    continue;
                }

                if (sensor.SensorType == SensorType.Temperature)
                {
                    var value = sensor.Value.Value;
                    if (!IsPlausibleTemp(value))
                    {
                        continue;
                    }

                    var isPackageSensor = (sensor.Name ?? string.Empty).IndexOf("package", StringComparison.OrdinalIgnoreCase) >= 0;
                    gpuAggregate.Add(value, isPackageSensor, sensor.Name);
                }
                else if (sensor.SensorType == SensorType.Load)
                {
                    var load = sensor.Value.Value;
                    if (load > maxLoad)
                    {
                        maxLoad = load;
                    }
                }
            }

            gpuCandidates.Add(new GpuCandidate(hardware.HardwareType, maxLoad, gpuAggregate));
        }

        private static float? GetActiveGpuTemp(List<GpuCandidate> gpuCandidates)
        {
            if (gpuCandidates.Count == 0)
            {
                return null;
            }

            var bestIndex = -1;
            for (var i = 0; i < gpuCandidates.Count; i++)
            {
                if (bestIndex < 0 || IsBetterGpu(gpuCandidates[i], gpuCandidates[bestIndex]))
                {
                    bestIndex = i;
                }
            }

            return bestIndex >= 0 ? gpuCandidates[bestIndex].Temp.GetAverage() : null;
        }

        private static bool IsBetterGpu(GpuCandidate candidate, GpuCandidate current)
        {
            if (candidate.MaxLoad > current.MaxLoad + 0.01f)
            {
                return true;
            }

            if (current.MaxLoad > candidate.MaxLoad + 0.01f)
            {
                return false;
            }

            var candidateDiscrete = IsDiscreteGpu(candidate.Type);
            var currentDiscrete = IsDiscreteGpu(current.Type);
            if (candidateDiscrete != currentDiscrete)
            {
                return candidateDiscrete;
            }

            var candidateTemp = candidate.Temp.GetAverage();
            var currentTemp = current.Temp.GetAverage();
            if (candidateTemp.HasValue && !currentTemp.HasValue)
            {
                return true;
            }

            return false;
        }

        private static bool IsDiscreteGpu(HardwareType type)
        {
            return type == HardwareType.GpuNvidia || type == HardwareType.GpuAmd;
        }

        private static bool IsPlausibleTemp(float value)
        {
            return value > MinValidTempC && value < MaxValidTempC;
        }

        private struct TempAggregate
        {
            private readonly string _preferredSensorName;
            private float _sum;
            private int _count;
            private float _packageSum;
            private int _packageCount;
            private float _preferredSum;
            private int _preferredCount;

            public TempAggregate(string preferredSensorName = null)
            {
                _preferredSensorName = preferredSensorName;
                _sum = 0f;
                _count = 0;
                _packageSum = 0f;
                _packageCount = 0;
                _preferredSum = 0f;
                _preferredCount = 0;
            }

            public void Add(float value, bool isPackage, string sensorName)
            {
                _sum += value;
                _count++;

                if (!string.IsNullOrWhiteSpace(_preferredSensorName) &&
                    string.Equals(sensorName, _preferredSensorName, StringComparison.OrdinalIgnoreCase))
                {
                    _preferredSum += value;
                    _preferredCount++;
                }

                if (isPackage)
                {
                    _packageSum += value;
                    _packageCount++;
                }
            }

            public float? GetAverage()
            {
                if (_preferredCount > 0)
                {
                    return _preferredSum / _preferredCount;
                }

                if (_packageCount > 0)
                {
                    return _packageSum / _packageCount;
                }

                if (_count > 0)
                {
                    return _sum / _count;
                }

                return null;
            }
        }

        private readonly struct GpuCandidate
        {
            public GpuCandidate(HardwareType type, float maxLoad, TempAggregate temp)
            {
                Type = type;
                MaxLoad = maxLoad;
                Temp = temp;
            }

            public HardwareType Type { get; }
            public float MaxLoad { get; }
            public TempAggregate Temp { get; }
        }
    }

    internal sealed class TemperatureSnapshot
    {
        public TemperatureSnapshot(float? cpuCelsius, float? gpuCelsius, string error, string sourceNamespace)
        {
            CpuCelsius = cpuCelsius;
            GpuCelsius = gpuCelsius;
            Error = error;
            SourceNamespace = sourceNamespace;
        }

        public float? CpuCelsius { get; }
        public float? GpuCelsius { get; }
        public string Error { get; }
        public string SourceNamespace { get; }
    }
}
