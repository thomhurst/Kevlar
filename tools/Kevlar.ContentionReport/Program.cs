using System.Text.Json;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

if (args.Length != 1 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: Kevlar.ContentionReport <trace.nettrace>");
    return 1;
}

var etlx = TraceLog.CreateFromEventPipeDataFile(args[0]);
using var log = new TraceLog(etlx);
var waiting = new Dictionary<(int ProcessId, int ThreadId), string>();
var counts = new Dictionary<string, (int Count, double Milliseconds)>();
var starts = 0;
var unmatchedStops = 0;
var overwrittenStarts = 0;
foreach (var item in log.Events)
{
    var thread = (item.ProcessID, item.ThreadID);
    if (item is ContentionStartTraceData)
    {
        var frames = new List<string>();
        for (var stack = item.CallStack(); stack is not null; stack = stack.Caller)
        {
            frames.Add(stack.CodeAddress.FullMethodName);
        }
        var text = frames.Count == 0 ? "[unresolved]" : string.Join("\n", frames);
        if (waiting.ContainsKey(thread))
        {
            overwrittenStarts++;
        }
        waiting[thread] = text;
        var value = counts.GetValueOrDefault(text);
        counts[text] = (value.Count + 1, value.Milliseconds);
        starts++;
    }
    else if (item is ContentionStopTraceData stop)
    {
        if (!waiting.Remove(thread, out var stack))
        {
            unmatchedStops++;
            continue;
        }
        var value = counts[stack];
        counts[stack] = (value.Count, value.Milliseconds + stop.DurationNs / 1_000_000);
    }
}

Console.WriteLine(JsonSerializer.Serialize(new
{
    EventsLost = log.EventsLost,
    Starts = starts,
    UnmatchedStops = unmatchedStops,
    UnmatchedStarts = waiting.Count + overwrittenStarts,
    Stacks = counts.OrderByDescending(pair => pair.Value.Milliseconds).Select(pair => new
    {
        pair.Value.Count,
        pair.Value.Milliseconds,
        Stack = pair.Key,
    }).ToArray(),
}, new JsonSerializerOptions { WriteIndented = true }));
return 0;
