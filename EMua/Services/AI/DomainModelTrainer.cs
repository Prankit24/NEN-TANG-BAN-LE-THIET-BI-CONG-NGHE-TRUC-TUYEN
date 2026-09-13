using EMua.Models.AI;
using Microsoft.ML;

namespace EMua.Services.AI
{
    public static class DomainModelTrainer
    {
        public static void Train(string dataPath, string modelPath)
        {
            var mlContext = new MLContext(seed: 1);

            // Đọc dữ liệu từ file CSV
            var data = mlContext.Data.LoadFromTextFile<DomainTrainingData>(
                path: dataPath,
                hasHeader: true,
                separatorChar: ',');

            // Chuyển văn bản thành vector số
            var pipeline = mlContext.Transforms.Text
                .FeaturizeText(
                    outputColumnName: "Features",
                    inputColumnName: nameof(DomainTrainingData.Text))

                // Train mô hình phân loại 2 lớp
                .Append(
                    mlContext.BinaryClassification.Trainers
                        .SdcaLogisticRegression(
                            labelColumnName: nameof(DomainTrainingData.Label),
                            featureColumnName: "Features"));

            // Train
            var model = pipeline.Fit(data);

            // Tạo folder nếu chưa có
            var directory = Path.GetDirectoryName(modelPath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Lưu model
            mlContext.Model.Save(
                model,
                data.Schema,
                modelPath);

            Console.WriteLine("Train Domain Model thành công!");
        }
    }
}