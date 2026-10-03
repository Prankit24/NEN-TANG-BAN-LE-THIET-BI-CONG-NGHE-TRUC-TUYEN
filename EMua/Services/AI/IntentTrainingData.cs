using Microsoft.ML.Data;

namespace EMua.Models.AI;

public class IntentTrainingData
{
    [LoadColumn(0)]
    public string Text { get; set; } = string.Empty;

    [LoadColumn(1)]
    public string Label { get; set; } = string.Empty;
}

