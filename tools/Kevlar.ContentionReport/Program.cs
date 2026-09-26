using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using System.Text.Json;

var etlx = TraceLog.CreateFromEventPipeDataFile(args[0]);
using var log = new TraceLog(etlx);
var waiting = new Dictionary<int, string>();
var counts = new Dictionary<string, (int Count, double Milliseconds)>();
var allStarts = 0;
var unmatchedStops = 0;
foreach (var item in log.Events)
{
    if (item is ContentionStartTraceData)
    {
        var frames = new List<string>();
        for (var stack = item.CallStack(); stack is not null; stack = stack.Caller)
            frames.Add(stack.CodeAddress.FullMethodName);
        var text = string.Join("\n", frames);
        if (text.Length == 0) text = "[unresolved]";
        waiting[item.ThreadID] = text;
        var value = counts.GetValueOrDefault(text);
        counts[text] = (value.Count + 1, value.Milliseconds);
        allStarts++;
    }
    else if (item is ContentionStopTraceData stop)
    {
        if (!waiting.Remove(item.ThreadID, out var stack)) { unmatchedStops++; continue; }
        var value = counts[stack];
        counts[stack] = (value.Count, value.Milliseconds + stop.DurationNs / 1_000_000);
    }
}
Console.WriteLine(JsonSerializer.Serialize(new { EventsLost = log.EventsLost, Starts = allStarts, UnmatchedStops = unmatchedStops,
    Stacks = counts.OrderByDescending(pair => pair.Value.Milliseconds).Select(pair => new { Count = pair.Value.Count, Milliseconds = pair.Value.Milliseconds, Stack = pair.Key }).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
