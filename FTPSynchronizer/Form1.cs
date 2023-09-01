using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FTPSynchronizer
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }
        Timer timer = new Timer();
        string serverAddress = "", ftpPort = "", ftpUsername = "", ftpPassword = "", ftpBackupFolder = "";
        NetworkCredential credential = new NetworkCredential();
        List<string> directories = new List<string>();
        NotifyIcon notifi = new NotifyIcon();
        private void Form1_Load(object sender, EventArgs e)
        {
            BeginInvoke(new MethodInvoker(delegate
            {
                Hide();
            }));
            ReadConfigFileAndCreateFTPDirectories();

            notifi.Icon = new Icon(Application.StartupPath + "\\sync.ico");
            notifi.Text = "FTPSynchronizer";
            notifi.Visible = true;

            timer.Interval = 1000 * 60;
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        public void ReadConfigFileAndCreateFTPDirectories()
        {
            if (File.Exists(Application.StartupPath + "\\config.txt"))
            {
                StreamReader sr = new StreamReader(Application.StartupPath + "\\config.txt", Encoding.GetEncoding("windows-1254"));
                List<string> lines = new List<string>(sr.ReadToEnd().Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).ToList());
                if (lines.Any() && lines[0].Contains(";"))
                {
                    serverAddress = lines[0].Split(';')[0].Replace("ftp://", "");
                    ftpPort = lines[0].Split(';')[1];
                    ftpUsername = lines[0].Split(';')[2];
                    ftpPassword = lines[0].Split(';')[3];
                    ftpBackupFolder = lines[0].Split(';')[4];
                    credential = new NetworkCredential(ftpUsername, ftpPassword);
                    directories.AddRange(lines);
                    directories.RemoveAt(0);

                    if (directories.Any())
                    {
                        foreach (string directory in directories)
                        {
                            string _directory = directory.Contains(";") ? directory.Split(';')[0] : directory;
                            string _ftpDirectory = ftpBackupFolder + "\\" + _directory.Substring(_directory.ToUpper().Contains("INETPUB\\VHOSTS") ? _directory.ToUpper().IndexOf("INETPUB\\VHOSTS") + 14 : _directory.IndexOf("\\") + 1);
                            string _fileQuery = directory.Contains(";") ? directory.Split(';')[1] : null;
                            List<string> _extensions = _fileQuery != null ? _fileQuery.Contains(",") ? _fileQuery.Split(',').ToList() : new List<string>() { _fileQuery } : new List<string>();
                            List<string> directoryFiles = Directory.GetFiles(_directory).Where(f => _extensions.Any() ? _extensions.Contains(f.Substring(f.LastIndexOf(".") + 1)) : true).ToList();

                            string _createdDirectory = "";
                            foreach (string folderName in _ftpDirectory.Split('\\').Where(d => d != "\\").ToList())
                            {
                                _createdDirectory += "\\" + folderName;
                                CreateFTPDirectory(_createdDirectory);
                            }
                        }
                    }
                }
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            timer.Stop();
            if (directories.Any())
            {
                List<string> files = new List<string>();
                foreach (string directory in directories)
                {
                    string _directory = directory.Contains(";") ? directory.Split(';')[0] : directory;
                    string _ftpDirectory = ftpBackupFolder + "\\" + _directory.Substring(_directory.ToUpper().Contains("INETPUB\\VHOSTS") ? _directory.ToUpper().IndexOf("INETPUB\\VHOSTS") + 14 : _directory.IndexOf("\\") + 1);
                    string _fileQuery = directory.Contains(";") ? directory.Split(';')[1] : null;
                    List<string> _extensions = _fileQuery != null ? _fileQuery.Contains(",") ? _fileQuery.Split(',').ToList() : new List<string>() { _fileQuery } : new List<string>();
                    List<string> directoryFiles = Directory.GetFiles(_directory).Where(f => _extensions.Any() ? _extensions.Contains(f.Substring(f.LastIndexOf(".") + 1)) : true).ToList();

                    string url = ("ftp://" + serverAddress + ":" + ftpPort + "\\" + _ftpDirectory).Replace("\\\\", "\\").Replace("\\", "/");
                    FtpWebRequest request = (FtpWebRequest)WebRequest.Create(url);
                    request.Method = WebRequestMethods.Ftp.ListDirectory;
                    request.Credentials = credential;
                    FtpWebResponse response = (FtpWebResponse)request.GetResponse();
                    Stream responseStream = response.GetResponseStream();
                    StreamReader reader = new StreamReader(responseStream);
                    string names = reader.ReadToEnd();

                    reader.Close();
                    response.Close();

                    List<string> ftpDirectoryFiles = names.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Select(f => f.Contains("/") || f.Contains("\\") ? f.Substring(f.LastIndexOf(f.Contains("/") ? "/" : "\\") + 1) : f).ToList();
                    files.AddRange(directoryFiles.Select(f => f.Substring(f.LastIndexOf("\\") + 1)).Where(f => !ftpDirectoryFiles.Contains(f)).Select(f => _directory + "\\" + f).ToList());
                }

                timer.Interval = 1000 * 60 * (files.Count >= 100 ? 1 : 10);
                UploadFiles(files.Take(100).ToList());
            }
            timer.Start();
        }

        private bool CreateFTPDirectory(string directory)
        {
            try
            {
                string url = ("ftp://" + serverAddress + ":" + ftpPort + "\\" + directory).Replace("\\\\", "\\").Replace("\\", "/");
                FtpWebRequest requestDir = (FtpWebRequest)WebRequest.Create(new Uri(url));
                requestDir.Method = WebRequestMethods.Ftp.MakeDirectory;
                requestDir.Credentials = credential;
                requestDir.UsePassive = true;
                requestDir.UseBinary = true;
                requestDir.KeepAlive = false;
                FtpWebResponse response = (FtpWebResponse)requestDir.GetResponse();
                Stream ftpStream = response.GetResponseStream();

                ftpStream.Close();
                response.Close();

                return true;
            }
            catch (WebException ex)
            {
                FtpWebResponse response = (FtpWebResponse)ex.Response;
                if (response.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
                {
                    response.Close();
                    return true;
                }
                else
                {
                    response.Close();
                    return false;
                }
            }
        }

        public void UploadFiles(List<string> files)
        {
            if (files.Any())
            {
                using (var client = new WebClient())
                {
                    client.Credentials = credential;
                    foreach (string file in files)
                    {
                        try
                        {
                            string ftpPath = ("ftp://" + serverAddress + ":" + ftpPort + "\\" + ftpBackupFolder + "\\" + file.Substring(file.ToUpper().Contains("INETPUB\\VHOSTS") ? file.ToUpper().IndexOf("INETPUB\\VHOSTS") + 14 : file.IndexOf("\\") + 1)).Replace("\\\\", "\\").Replace("\\", "/");
                            client.UploadFile(ftpPath, WebRequestMethods.Ftp.UploadFile, file.Replace("\\\\", "\\").Replace("\\", "/"));
                            long fileSize = new FileInfo(file).Length;
                            if (fileSize != GetFileSize(ftpPath))
                            {
                                DeleteFile(ftpPath);
                            }
                        }
                        catch
                        {
                            continue;
                        }
                    }
                }
            }
        }

        private void DeleteFile(string ftpPath)
        {
            FtpWebRequest requestDir = (FtpWebRequest)WebRequest.Create(new Uri(ftpPath));
            requestDir.Credentials = credential;
            requestDir.Method = WebRequestMethods.Ftp.DeleteFile;
            FtpWebResponse response = (FtpWebResponse)requestDir.GetResponse();
            response.Close();
        }

        public long GetFileSize(string ftpPath)
        {
            FtpWebRequest requestDir = (FtpWebRequest)WebRequest.Create(new Uri(ftpPath));
            requestDir.Credentials = credential;
            requestDir.Method = WebRequestMethods.Ftp.GetFileSize;
            FtpWebResponse response = (FtpWebResponse)requestDir.GetResponse();
            long size = response.ContentLength;
            response.Close();

            return size;
        }
    }
}
