using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using TMPro;
using UnityEngine;

/// <summary>
/// Acquired on 3.11.2022 from https://stackoverflow.com/questions/56684306/how-to-read-system-usage-cpu-ram-etc
/// </summary>

public class DebugCpuUsage : MonoBehaviour
{
    [Header("Components")]

    [SerializeField] private TMP_Text cpuCounterText;

    [Header("Settings")]

    [Tooltip("In which interval should the CPU usage be updated?")]
    [SerializeField] private float updateInterval = 1;

    [Tooltip("The amount of physical CPU cores")]
    [SerializeField] private int processorCount;


    [Header("Output")]
    public float CpuUsage;

    private Thread _cpuThread;
    private float _lasCpuUsage = -1f;
    private bool _cpuUsageReady;

    private void Start()
    {
        Application.runInBackground = true;

        cpuCounterText.text = "CPU 0%";

        // setup the thread
        _cpuThread = new Thread(UpdateCPUUsage)
        {
            IsBackground = true,
            // we don't want that our measurement thread
            // steals performance
            Priority = System.Threading.ThreadPriority.BelowNormal
        };

        // start the cpu usage thread
        _cpuThread.Start();
    }

    private void OnValidate()
    {
        // We want only the physical cores but usually
        // this returns the twice as many virtual core count
        //
        // if this returns a wrong value for you comment this method out
        // and set the value manually
        processorCount = SystemInfo.processorCount / 2;
    }

    private void OnDestroy()
    {
        // Just to be sure kill the thread if this object is destroyed
        _cpuThread?.Abort();
    }

    private void Update()
    {
        // for more efficiency skip if nothing has changed
        if (Mathf.Approximately(_lasCpuUsage, CpuUsage)) return;

        // the first two values will always be "wrong"
        // until _lastCpuTime is initialized correctly
        // so simply ignore values that are out of the possible range

        if (!_cpuUsageReady) return;

        if (_lasCpuUsage >= 0) {
            // I used a float instead of int for the % so use the ToString you like for displaying it
            cpuCounterText.text = "CPU " + CpuUsage.ToString("F0") + "% / " + processorCount*100 + "%";
        }

        // Update the value of _lasCpuUsage
        _lasCpuUsage = CpuUsage;
    }

    /// <summary>
    /// Runs in Thread
    /// </summary>
    private void UpdateCPUUsage()
    {
        var lastCpuTime = new TimeSpan(0);

        // This is ok since this is executed in a background thread
        while (true)
        {
            var cpuTime = new TimeSpan(0);

            // Get a list of all running processes in this PC
            var AllProcesses = Process.GetProcesses();

            // Sum up the total processor time of all running processes
            cpuTime = AllProcesses.Aggregate(cpuTime, (current, process) => current + process.TotalProcessorTime);

            // get the difference between the total sum of processor times
            // and the last time we called this
            var newCPUTime = cpuTime - lastCpuTime;

            // update the value of _lastCpuTime
            lastCpuTime = cpuTime;

            // The value we look for is the difference, so the processor time all processes together used
            // since the last time we called this divided by the time we waited
            // Then since the performance was optionally spread equally over all physical CPUs
            // we also divide by the physical CPU count
            CpuUsage = 100f * (float)newCPUTime.TotalSeconds / updateInterval;

            _cpuUsageReady = true;

            // Wait for UpdateInterval
            Thread.Sleep(Mathf.RoundToInt(updateInterval * 1000));
        }
    }
}
