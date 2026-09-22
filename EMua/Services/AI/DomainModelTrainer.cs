using EMua.Models.AI;
using Microsoft.ML;

namespace EMua.Services.AI
{
    public static class DomainModelTrainer
    {
        public static void Train(string dataPath, string modelPath)
        {
            var mlContext = new MLContext(seed: 1);
            var data = mlContext.Data.LoadFromTextFile<DomainTrainingData>(
                path: dataPath,
                hasHeader: true,
                separatorChar: ',');
            var pipeline = mlContext.Transforms.Text
                .FeaturizeText(
                    outputColumnName: "Features",
                    inputColumnName: nameof(DomainTrainingData.Text))
                .Append(
                    mlContext.BinaryClassification.Trainers
                        .SdcaLogisticRegression(
                            labelColumnName: nameof(DomainTrainingData.Label),
                            featureColumnName: "Features"));
            var model = pipeline.Fit(data);
            var directory = Path.GetDirectoryName(modelPath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            mlContext.Model.Save(
                model,
                data.Schema,
                modelPath);

            Console.WriteLine("Train Domain Model thành công!");
        }
    }
}