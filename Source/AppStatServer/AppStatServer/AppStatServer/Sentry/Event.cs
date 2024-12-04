using System.Text;

namespace AppStatServer.Sentry;

public class ExceptionValue
{
    public string type { get; set; }
    public string value { get; set; }
    public string module { get; set; }
    public int thread_id { get; set; }
}

public class StacktraceFrame
{
    public string function { get; set; }
    public bool in_app { get; set; }
    public string package { get; set; }
    public string instruction_addr { get; set; }
    public string addr_mode { get; set; }
    public string function_id { get; set; }
    public string filename { get; set; }
    public int? lineno { get; set; }
    public int? colno { get; set; }
    public string abs_path { get; set; }
}

public class StacktraceValue
{
    public List<StacktraceFrame>? frames { get; set; }

    public override string ToString()
    {
        if (frames == null)
            return "";
        return FormatStackTrace(frames);
    }

    public static string FormatStackTrace(List<StacktraceFrame> stackTraceFrames)
    {
        StringBuilder formattedStackTrace = new StringBuilder();
        int frameIndex = 1;
        foreach (var frame in stackTraceFrames)
        {
            if (frameIndex == 1)
            {
                formattedStackTrace.AppendLine($"Exception in thread \"{frame.package}\" {frame.function}");
            }
            else
            {
                formattedStackTrace.AppendLine($"\tat {frame.package}.{frame.function}({frame.filename}:{frame.lineno})");
            }
            frameIndex++;
        }
        return formattedStackTrace.ToString();
    }
}

public class ExceptionInfo
{
    public List<ExceptionValue> values { get; set; }
}

public class ThreadValue
{
    public int id { get; set; }
    public string name { get; set; }
    public bool crashed { get; set; }
    public bool current { get; set; }
    public StacktraceValue stacktrace { get; set; }
}

public class Threads
{
    public List<ThreadValue> values { get; set; }
}

public class CurrentCulture
{
    public string name { get; set; }
    public string display_name { get; set; }
    public string calendar { get; set; }
}

public class DynamicCode
{
    public bool Compiled { get; set; }
    public bool Supported { get; set; }
}

public class MemoryInfo
{
    public int allocated_bytes { get; set; }
    public long high_memory_load_threshold_bytes { get; set; }
    public long total_available_memory_bytes { get; set; }
    public int finalization_pending_count { get; set; }
    public bool compacted { get; set; }
    public bool concurrent { get; set; }
    public List<int> pause_durations { get; set; }
}

public class ThreadPoolInfo
{
    public int min_worker_threads { get; set; }
    public int min_completion_port_threads { get; set; }
    public int max_worker_threads { get; set; }
    public int max_completion_port_threads { get; set; }
    public int available_worker_threads { get; set; }
    public int available_completion_port_threads { get; set; }
}

public class AppInfo
{
    public string type { get; set; }
    public DateTime app_start_time { get; set; }
    public bool in_foreground { get; set; }
}

public class Device
{
    public string type { get; set; }
    public string timezone { get; set; }
    public string timezone_display_name { get; set; }
    public DateTime boot_time { get; set; }
}

public class Os
{
    public string type { get; set; }
    public string raw_description { get; set; }
}

public class Runtime
{
    public string type { get; set; }
    public string name { get; set; }
    public string version { get; set; }
    public string raw_description { get; set; }
    public string identifier { get; set; }
}

public class TraceInfo
{
    public string type { get; set; }
    public string span_id { get; set; }
    public string trace_id { get; set; }
}

public class Package
{
    public string name { get; set; }
    public string version { get; set; }
}

public class SdkInfo
{
    public List<Package> packages { get; set; }
    public string name { get; set; }
    public string version { get; set; }
}

public class Image
{
    public string type { get; set; }
    public string debug_id { get; set; }
    public string debug_checksum { get; set; }
    public string debug_file { get; set; }
    public string code_id { get; set; }
    public string code_file { get; set; }
}

public class DebugMeta
{
    public List<Image> images { get; set; }
}

public class Contexts
{
    public CurrentCulture CurrentCulture { get; set; }
    public DynamicCode DynamicCode { get; set; }
    public MemoryInfo MemoryInfo { get; set; }
    public ThreadPoolInfo ThreadPoolInfo { get; set; }
    public AppInfo app { get; set; }
    public Device device { get; set; }
    public Os os { get; set; }
    public Runtime runtime { get; set; }
    public TraceInfo trace { get; set; }
}

public class User
{
    public string ip_address { get; set; }
    public string id { get; set; }
    public string username { get; set; }
}

public class LogEntry
{
    public string message { get; set; }
}

public class EventEntry
{
    public string event_id { get; set; }
    public DateTime timestamp { get; set; }
    public LogEntry logentry { get; set; }
    public string platform { get; set; }
    public string release { get; set; }
    public ExceptionInfo exception { get; set; }
    public Threads threads { get; set; }
    public string level { get; set; }
    public Dictionary<string, object> request { get; set; }
    public Contexts contexts { get; set; }
    public User user { get; set; }
    public string environment { get; set; }
    public SdkInfo sdk { get; set; }
    public DebugMeta debug_meta { get; set; }
}