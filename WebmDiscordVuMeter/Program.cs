using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebmDiscordVuMeter
{
    internal static class Program
    {
        
        public struct VideoDetails
        {
            public int Width, Height, FPS;
        }
       
        public static int AudioGraphRate = 30;
        
        public struct ToProcess
        {
            public int Width, Height;
            public string File;
        }
        static void Main(string[] args)
        {
            var videoFile = @"C:\Users\Workspace\Desktop\Untitled3.mp4";

            var videoDetails = GetMaxVideoSize(videoFile);

            if(videoDetails.Value.FPS / AudioGraphRate < 1)
            {
                AudioGraphRate = videoDetails.Value.FPS;
            }

            var staticRatio = videoDetails.Value.FPS / AudioGraphRate;


            if (videoDetails.Key)
            {
                Console.WriteLine($"Details: {videoDetails.Value.Width}x{videoDetails.Value.Height} at {videoDetails.Value.FPS}fps");

                if(GetAudioGraph(videoFile, out List<float> graph, out float minVal, out float maxVal))
                {
                    Console.WriteLine($"{graph.Count / AudioGraphRate} seconds");

                    Directory.Delete("TempOriginalFrames", true);
                    Directory.CreateDirectory("TempOriginalFrames");

                    var separateFramesFFMPEG = new Process()
                    {
                        StartInfo = new ProcessStartInfo()
                        {
                            FileName = "ffmpeg",
                            Arguments = $"-y -i \"{videoFile}\" -qscale:v 29 \"TempOriginalFrames/%d.jpg\"",
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    separateFramesFFMPEG.ErrorDataReceived += delegate (object _, DataReceivedEventArgs e)
                    {
                        if (e.Data != null)
                        {
                            Debug.WriteLine(e.Data);
                        }
                    };
                    separateFramesFFMPEG.Start();
                    separateFramesFFMPEG.BeginErrorReadLine();

                    separateFramesFFMPEG.WaitForExit();

                    var files = Directory.GetFiles("TempOriginalFrames").OrderBy(x => int.Parse(Path.GetFileNameWithoutExtension(x))).ToArray();
                    Console.WriteLine($"Got {files.Length} frames");

                    Directory.Delete("TempResizedFrames", true);
                    Directory.CreateDirectory("TempResizedFrames");
                    
                    var listToProcess = new List<ToProcess>();
                    var done = 0;
                    for (int i = 0; i < graph.Count; i++)
                    {
                        for (int y = 0; y < staticRatio; y++)
                        {
                            try
                            {
                                var frame = files[listToProcess.Count];

                                var width = videoDetails.Value.Width;  //(int)(videoDetails.Value.Width * graph[i]); 
                                var height = (int)graph[i].Map(minVal, maxVal, 1, videoDetails.Value.Width);//(int)(videoDetails.Value.Height * graph[i]);
                                Console.WriteLine($"{width}x{height}");
                                listToProcess.Add(new ToProcess() { File = frame, Width = width, Height = height });
                            }
                            catch { }
                            

                        }
                    }
                    Console.WriteLine($"To process: {graph.Count} {videoDetails.Value.FPS} {AudioGraphRate} {staticRatio} {files.Length} {listToProcess.Count}");

                    
                    var res = Parallel.ForEach(listToProcess, new ParallelOptions() { MaxDegreeOfParallelism = 99 }, toProcess =>
                    {
                        var resizeFFMPEG = new Process()
                        {
                            StartInfo = new ProcessStartInfo()
                            {
                                FileName = "ffmpeg",//-b:v 1M -crf 10
                                Arguments = $"-y -i \"{toProcess.File}\" -c:v vp8 -b:v 1K -vf scale={toProcess.Width}x{toProcess.Height} -aspect {toProcess.Width}:{toProcess.Height} -r {videoDetails.Value.FPS} -f webm \"TempResizedFrames/{Path.GetFileNameWithoutExtension(toProcess.File)}.webm\"",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                       
                        resizeFFMPEG.Start();
                        resizeFFMPEG.WaitForExit();
                    });

                    do { Thread.Sleep(1000); } while (!res.IsCompleted);
                    
                    

                    Console.WriteLine("Done generating parts");
                    
                    File.WriteAllLines("tempWebmFiles.txt", Directory.GetFiles("TempResizedFrames").OrderBy(x => int.Parse(Path.GetFileNameWithoutExtension(x))).Select(x => $"file '{x}'"));

                    var concatFFMPEG = new Process()
                    {
                        StartInfo = new ProcessStartInfo()
                        {
                            FileName = "ffmpeg",
                            Arguments = $"-y -f concat -safe 0 -i tempWebmFiles.txt -c copy mergedVideo.webm",
                            // RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    concatFFMPEG.Start();
                    concatFFMPEG.WaitForExit();
                    Console.WriteLine("done concat");

                    var getAudio = new Process()
                    {
                        StartInfo = new ProcessStartInfo()
                        {
                            FileName = "ffmpeg",
                            Arguments = $"-y -i \"{videoFile}\" -vn -c:a libvorbis originalAudio.webm",
                            // RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    getAudio.Start();
                    getAudio.WaitForExit();
                    Console.WriteLine("done convert only audio");

                    var concatAudio = new Process()
                    {
                        StartInfo = new ProcessStartInfo()
                        {
                            FileName = "ffmpeg",
                            Arguments = $"-y -i mergedVideo.webm -i originalAudio.webm -c copy \"done-{Path.GetFileNameWithoutExtension(videoFile)}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.webm\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    concatAudio.Start();
                    concatAudio.WaitForExit();

                    Console.WriteLine("done");

                }
                else
                {
                    Console.WriteLine("Failed to generate graph");
                }
            }
            else
            {
                Console.WriteLine("Failed to gather max res");
            }

            Thread.Sleep(-1);
        }
        public static float Map(this float value, float fromSource, float toSource, float fromTarget, float toTarget)
        {
            return (value - fromSource) / (toSource - fromSource) * (toTarget - fromTarget) + fromTarget;
        }
        public static KeyValuePair<bool, VideoDetails> GetMaxVideoSize(string videoFile)
        {
            var videoDetails = new VideoDetails();
            var success = 0;

            try
            {
                var ffprobe = new Process()
                {
                    StartInfo = new ProcessStartInfo()
                    {
                        FileName = "ffprobe",
                        Arguments = $"-v error -select_streams v -of default=noprint_wrappers=1:nokey=1 -show_entries stream=width,height,r_frame_rate \"{videoFile}\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                ffprobe.OutputDataReceived += delegate (object _, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                    {
                        var components = e.Data.Split(new string[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);

                        if (components.Length == 1)
                        {
                            Console.WriteLine("k " + e.Data);
                            if (videoDetails.Width == 0)
                            {
                                videoDetails.Width = int.Parse(e.Data);
                            }
                            else
                            {
                                if (videoDetails.Height == 0)
                                {
                                    videoDetails.Height = int.Parse(e.Data);
                                }
                                else
                                {
                                    var fpsFractionComponents = e.Data.Split(new string[] { "/" }, StringSplitOptions.RemoveEmptyEntries);
                                    videoDetails.FPS = int.Parse(fpsFractionComponents[0]) / int.Parse(fpsFractionComponents[1]);
                                }
                            }
                        }
                    }
                };
                ffprobe.Start();
                ffprobe.BeginOutputReadLine();

                ffprobe.WaitForExit();
            }
            catch
            {

            }

            return new KeyValuePair<bool, VideoDetails>(videoDetails.Width > 0 && videoDetails.Height > 0, videoDetails);
        }
        public static bool GetAudioGraph(string videoFile, out List<float> graph, out float min, out float max)
        {
            min = 0;
            max = 0;
            graph = new List<float>();

            var tempAudioFile = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(videoFile) + ".wav");

            var ffmpeg = new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = "ffmpeg",
                    Arguments = $"-y -i \"{videoFile}\" \"{tempAudioFile}\"",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            ffmpeg.ErrorDataReceived += delegate (object _, DataReceivedEventArgs e)
            {
                if (e.Data != null)
                {
                    Debug.WriteLine(e.Data);
                }
            };
            ffmpeg.Start();
            ffmpeg.BeginErrorReadLine();

            ffmpeg.WaitForExit();

            if (File.Exists(tempAudioFile))
            {
                var silenceDict = new Dictionary<int, bool>();
                using (AudioFileReader wave = new AudioFileReader(tempAudioFile))
                {
                    var samplesPerRate = wave.WaveFormat.SampleRate * wave.WaveFormat.Channels / AudioGraphRate;
                    var readBuffer = new float[samplesPerRate];
                    int samplesRead;

                    do
                    {
                        samplesRead = wave.Read(readBuffer, 0, samplesPerRate - samplesPerRate % 4);
                        if (samplesRead == 0) break;
                        var average = readBuffer.Take(samplesRead).Average();

                        if (average > max)
                        {
                            max = average;
                        }
                        if (average < min)
                        {
                            min = average;
                        }
                        //Console.WriteLine(average);
                        graph.Add(average);
                    } while (samplesRead > 0);
                }

                File.Delete(tempAudioFile);
                return graph.Count > 0;
            }

            return false;
        }
    }
}
