using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NAudio.Wave; // 使用NAudio库处理音频文件

public class AudioShuffler
{
    private static void Main(string[] args)
    {
        // 基础目录设置
        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string outputDir = Path.Combine(baseDirectory, "古神语输出");    // 最终输出目录
        string inputDir = Path.Combine(baseDirectory, "格式转换内部文件"); // 中间处理目录

        // 整理文件名格式
        new AudioShuffler().FileNameFormat();

        // 确保输出目录存在
        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
            Console.WriteLine($"创建输出目录: {outputDir}");
        }

        // 获取所有支持的音频文件（过滤非音频文件）
        var audioFiles = Directory.GetFiles(inputDir, "*.*")
            .Where(f => IsSupportedAudioFile(f))
            .ToArray();

        if (audioFiles.Length == 0)
        {
            Console.WriteLine("没有找到支持的音频文件");
            return;
        }

        // 初始化音频处理器
        var shuffler = new AudioShuffler();
        foreach (var file in audioFiles)
        {
            try
            {
                // 构造输出路径
                string fileName = Path.GetFileNameWithoutExtension(file);
                Random r = new Random();
                string outputPath = Path.Combine(outputDir, $"{fileName}{r.Next(0,9999)}.wav");
                
                Console.WriteLine($"正在处理: {file}");
                // 处理音频并保存（使用80毫秒的块大小）
                shuffler.ShuffleAudio(file, outputPath, 200);
                Console.WriteLine($"已保存到: {outputPath}");
                
                File.Delete(file); // 删除中间处理文件
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理文件 {file} 出错: {ex.Message}");
            }
        }
        Console.WriteLine("全部处理完成!");
    }

    /// <summary>
    /// 音频混洗核心方法（增加音量随机化）
    /// </summary>
    /// <param name="inputPath">输入文件路径</param>
    /// <param name="outputPath">输出文件路径</param>
    /// <param name="chunkMs">分块时长（毫秒）</param>
    public void ShuffleAudio(string inputPath, string outputPath, int chunkMs)
    {
        // 使用 MediaFoundationReader 读取 MP3 文件（需要 Windows 8+）
        using (var reader = new MediaFoundationReader(inputPath))
        {
            // 转换为标准 16-bit PCM 格式
            var targetFormat = new WaveFormat(reader.WaveFormat.SampleRate, 16, reader.WaveFormat.Channels);
            using (var convertedStream = WaveFormatConversionStream.CreatePcmStream(reader))
            using (var resampler = new MediaFoundationResampler(convertedStream, targetFormat))
            {
                var format = targetFormat;
                int bytesPerChunk = CalculateChunkSize(format, chunkMs);


                var chunks = new List<byte[]>();
                byte[] buffer = new byte[bytesPerChunk];

                int bytesRead;
                while ((bytesRead = convertedStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    int bytesPerSample = format.BitsPerSample / 8;
                    if (bytesRead % bytesPerSample != 0)
                    {
                        Console.WriteLine($"警告: 跳过不完整块 (块大小: {bytesRead}, 采样点大小: {bytesPerSample})");
                        continue;
                    }

                    var chunk = new byte[bytesRead];
                    Array.Copy(buffer, chunk, bytesRead);

                    Random r = new Random();
                    float volumeFactor = (float)(r.NextDouble() * 1.8 + 0.5);

                    AdjustVolume(chunk, format, volumeFactor);

                    chunks.Add(chunk);
                }

                Shuffle(chunks);

                using (var writer = new WaveFileWriter(outputPath, format))
                {
                    foreach (var chunk in chunks)
                        writer.Write(chunk, 0, chunk.Length);
                }
            }
        }
    }
    
    /// <summary>
    /// 计算块字节大小（确保块对齐到采样点）
    /// </summary>
    /// <param name="format">音频格式信息</param>
    /// <param name="milliseconds">期望的块时长</param>
    /// <returns>对齐后的字节大小</returns>
    private int CalculateChunkSize(WaveFormat format, int milliseconds)
    {
        // 计算每毫秒的字节数（基于采样率、位深度和声道数）
        int bytesPerMillisecond = format.AverageBytesPerSecond / 1000;
        int chunkSize = milliseconds * bytesPerMillisecond;

        // 确保块大小是对采样点大小的整数倍
        int bytesPerSample = format.BitsPerSample / 8; // 每个采样的字节数
        chunkSize -= chunkSize % (bytesPerSample * format.Channels); // 对齐到采样点

        return chunkSize;
    }

    
    /// <summary>
    /// 调整音频块的音量
    /// </summary>
    /// <param name="chunk">音频块数据</param>
    /// <param name="format">音频格式信息</param>
    /// <param name="volumeFactor">音量缩放因子</param>
    private void AdjustVolume(byte[] chunk, WaveFormat format, float volumeFactor)
    {
        // 根据音频格式解析采样点
        int bytesPerSample = format.BitsPerSample / 8; // 每个采样的字节数
        int sampleCount = chunk.Length / bytesPerSample; // 总采样点数

        for (int i = 0; i < sampleCount; i++)
        {
            int sampleOffset = i * bytesPerSample;

            // 解码当前采样点的值
            short sampleValue;
            try
            {
                sampleValue = BitConverter.ToInt16(chunk, sampleOffset);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"无法解码采样点 (偏移: {sampleOffset}, 块大小: {chunk.Length})", ex);
            }

            // 应用音量缩放
            float adjustedValue = sampleValue * volumeFactor;

            // 确保调整后的值在合法范围内
            adjustedValue = Math.Clamp(adjustedValue, short.MinValue, short.MaxValue);

            // 将调整后的值重新编码回字节数组
            byte[] adjustedBytes = BitConverter.GetBytes((short)adjustedValue);
            Array.Copy(adjustedBytes, 0, chunk, sampleOffset, bytesPerSample);
        }
    }
    
    /// <summary>
    /// 使用加密安全随机数生成器打乱列表顺序
    /// </summary>
    /// <typeparam name="T">列表元素类型</typeparam>
    /// <param name="list">需要打乱顺序的列表</param>
    private void Shuffle<T>(IList<T> list)
    {
        int n = list.Count;
        if (n <= 1) return; // 如果列表长度小于等于1，无需打乱

        using (var rng = RandomNumberGenerator.Create()) // 创建加密安全随机数生成器
        {
            byte[] randomBytes = new byte[4]; // 用于存储随机字节
            for (int i = n - 1; i > 0; i--)
            {
                // 生成一个 [0, i] 范围内的随机索引
                rng.GetBytes(randomBytes); // 填充随机字节
                int k = BitConverter.ToInt32(randomBytes, 0) % (i + 1);
                if (k < 0) k += (i + 1); // 确保索引非负

                // 交换元素
                (list[k], list[i]) = (list[i], list[k]);
            }
        }
    }

    /// <summary>
    /// 文件名整理方法
    /// 1. 创建必要的目录
    /// 2. 将源文件移动到隐藏的处理目录
    /// 3. 统一重命名为Input+编号的格式
    /// </summary>
    public void FileNameFormat()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string sourceDir = Path.Combine(baseDir, "音频源文件");      // 用户原始文件目录
        string processDir = Path.Combine(baseDir, "格式转换内部文件"); // 隐藏处理目录

        try
        {
            // 确保源目录存在
            Directory.CreateDirectory(sourceDir);
            
            // 创建并隐藏处理目录
            if (!Directory.Exists(processDir))
            {
                var dir = Directory.CreateDirectory(processDir);
                dir.Attributes |= FileAttributes.Hidden; // 设置为隐藏目录
            }

            // 处理源目录中的文件
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                // 跳过不支持的文件类型
                if (!IsSupportedAudioFile(file)) continue;

                // 生成序列化文件名
                string ext = Path.GetExtension(file); // 保留原始扩展名
                int count = Directory.GetFiles(processDir).Length + 1; // 当前文件数+1
                string newName = $"Input{count}{ext}";
                string dest = Path.Combine(processDir, newName);
                
                // 移动并重命名文件
                File.Move(file, dest);
                Console.WriteLine($"已整理文件: {dest}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"整理文件名出错: {ex.Message}");
        }
    }

    /// <summary>
    /// 检查文件是否为支持的音频格式
    /// </summary>
    /// <param name="file">文件路径</param>
    /// <returns>是否支持</returns>
    private static bool IsSupportedAudioFile(string file)
    {
        // 支持格式列表（可扩展）
        string[] supported = { 
            ".wav",  // 波形音频
            ".mp3",  // MPEG-1 Layer III
            ".aiff", // 音频交换文件格式
            ".flac", // 无损音频
            ".wma",  // Windows媒体音频
            ".m4a"   // AAC音频
        };
        return supported.Contains(Path.GetExtension(file).ToLower());
    }
}