﻿using System;
using System.Net;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Linq.Expressions;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Diagnostics;
using PutioApi;

namespace putio
{
    public partial class FormPutioManager : Form
    {
        public FormPutioManager()
        {
            InitializeComponent();
            InitializeControls();
            InitializeAsync();
        }

        private void InitializeControls()
        {
            OAuthToken = Properties.Settings.Default.OAuthToken;

            filemgr = new Files(OAuthToken);
            zipmgr = new Zips(OAuthToken);
            trfrmgr = new Transfers(OAuthToken);

            var root = new PutioFile("0", "putio files");

            treeViewPutioFiles.Nodes.Clear();
            rootnode = treeViewPutioFiles.Nodes.Add("putio files");
            rootnode.Tag = root;

            treeViewPutioFiles.ShowNodeToolTips = Properties.Settings.Default.ShowToolTips;
            treeViewPutioFiles.SelectedNode = treeViewPutioFiles.Nodes[0];


            splitContainerFiles.Panel2Collapsed = Properties.Settings.Default.ShowAutoDownloads;
            splitContainer1.Panel2Collapsed = Properties.Settings.Default.ShowManager;

            TimeWorker.DoWork += TimeWorker_DoWork;
            TimeWorker.ProgressChanged += TimeWorker_ReportProgress;
            TimeWorker.WorkerReportsProgress = true;
            TimeWorker.WorkerSupportsCancellation = true;
            TimeWorker.RunWorkerCompleted += TimeWorker_Complete;
        }

        private void InitializeAsync()
        {
            for (int i = 0; i < ParallelDownloads; i++)
            {
                WebClient client = new WebClient();
                client.DownloadProgressChanged += WebClient_DownloadProgressChanged;
                client.DownloadFileCompleted += WebClient_DownloadDataCompleted;
                WebClients.Add(client);
            }
            treeViewPutioFiles.NodeMouseClick += (sender, args) => treeViewPutioFiles.SelectedNode = args.Node;
        }

        public string OAuthToken;
        public string OAuthParamater;
        public string DownloadPath;
        public TreeNode rootnode;
        Files filemgr;
        Zips zipmgr;
        Transfers trfrmgr;
        int ParallelDownloads = Properties.Settings.Default.ParallelDownloads;

        private const string urlPutioApi = "https://api.put.io/v2/";
        List<WebClient> WebClients = new List<WebClient>();
        Queue<PutioFile> FileDownloads = new Queue<PutioFile>();

        BackgroundWorker TimeWorker = new BackgroundWorker();

        bool closeform = false;

        // Initalizers

        private void CstripDownload_Click(object sender, EventArgs e, PutioFile file)
        {
            if (file.webcilent.IsBusy)
            {
                Console.WriteLine("download canceled");
                file.webcilent.CancelAsync();
            }
        }

        private void WebClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            var FileDownload = e.UserState as PutioFile;
            DataGridViewRow FileQueRow = FileDownload.rowinque;
            string bytesrecieved = ((e.BytesReceived / 1024d) / 1024d).ToString("0.0");
            string totalbytes = ((e.TotalBytesToReceive / 1024d) / 1024d).ToString("0.0");
            UpdateCellValue(FileQueRow, "ColumnStatus", string.Format("{0} Mb / {1} Mb", bytesrecieved, totalbytes));
        }

        private void WebClient_DownloadDataCompleted(object sender, AsyncCompletedEventArgs e)
        {
            var FileDownload = e.UserState as PutioFile;
            DataGridViewRow FileQueRow = FileDownload.rowinque;

            if (e.Cancelled == true)
            {
                UpdateCellValue(FileQueRow, "ColumnStatus", "Canceled");
                File.Delete(FileDownload.download_path);
            }
            else if (e.Error != null)
            {
                UpdateCellValue(FileQueRow, "ColumnStatus", "Error");
            }
            else
            {
                UpdateCellValue(FileQueRow, "ColumnStatus", "Complete");
                if (Properties.Settings.Default.DeleteAfterDownload)
                    DeleteFile(FileDownload);
            }
            UpdateCellValue(FileQueRow, "ColumnCompleted", DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss"));
            DownloadFile();
        }

        private void TimeWorker_DoWork(object sender , DoWorkEventArgs e)
        {
            do
            {
                if (TimeWorker.CancellationPending)
                    break;
                int sleeptime = Properties.Settings.Default.AutoDownloadInterval * 1000;
                System.Threading.Thread.Sleep(sleeptime);
                TimeWorker.ReportProgress(0);
            } while (true);
        }

        private void TimeWorker_ReportProgress(object sender , ProgressChangedEventArgs e)
        {
            CheckAutoDownloads();
        }

        private void TimeWorker_Complete(object sender, RunWorkerCompletedEventArgs e)
        {
            Console.WriteLine("TimeWorker Stopped");
        }

        // Form Controls

        private async void FormPutioManager_Load(object sender, EventArgs e)
        {
            treeViewPutioFiles.SelectedNode = rootnode;
            //this.Icon.Size = new Size(32, 32);
            try
            {
                notifyIcon1.Visible = true;


                UpdateTreeView(await filemgr.List("0"), rootnode);
                var response = await filemgr.AccountInfo();
                UpdateStatusText(string.Format("Connected Account: " + response["username"].ToString()));
                TimeWorker.RunWorkerAsync();
                GetTransfers();
            }
            catch 
            {
                MessageBox.Show("the token provided did not work");
            }
        }

        private void FormPutioManager_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (closeform)
            {
                Properties.Settings.Default.Save();
                if (DownloadInProgress())
                {
                    DialogResult result = MessageBox.Show("Something is still downloading, continue closing?", "Download in progress", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (result == DialogResult.No)
                        e.Cancel = true;
                }
            }
            else
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void deleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode selectedNode = treeViewPutioFiles.SelectedNode;
            DeleteFile(selectedNode.Tag as PutioFile);
        }

        private void FormPutioManager_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                Hide();
                notifyIcon1.Visible = true;
            }
        }

        private void FormPutioManager_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            Show();
            this.WindowState = FormWindowState.Normal;
        }

        // Files Context Menu

        private void downloadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var putiofile = treeViewPutioFiles.SelectedNode.Tag as PutioFile;
            putiofile.downloadlink = new Uri(urlPutioApi + "files/" + putiofile.id + "/download?oauth_token=" + OAuthToken);
            QueueDownload(putiofile);
        }

        private async void zipDownloadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var selectedfile = treeViewPutioFiles.SelectedNode.Tag as PutioFile;

            string zipstatus = "";
            var createzip_response = await zipmgr.CreateZip(selectedfile.id);
            string zipid = JObject.Parse(createzip_response)["zip_id"].ToString();
            PutioFile zipfile = new PutioFile(zipid, selectedfile.name + ".zip");
            zipfile.file_type = "ZIP";

            do
            {
                var zip_properties = await zipmgr.GetZip(zipid);
                zipstatus = zip_properties["zip_status"].ToString();
                if (zipstatus == "DONE")
                    zipfile.downloadlink = new Uri(zip_properties["url"].ToString());
            } while (zipstatus != "DONE");

            QueueDownload(zipfile);
        }

        private async void propertiesToolStripMenuItem_Click(object sender, EventArgs e)
        {

            TreeNode selectednode = treeViewPutioFiles.SelectedNode;
            string id = selectednode.Tag.ToString();

            string message = null;

            var response = await filemgr.Get(id);

            foreach (var property in response)
            {
                message += property.Key + ": " + property.Value + Environment.NewLine;
            }

            selectednode.ToolTipText = message;

            var form_properties = new FormPutioProperties();
            form_properties.StartPosition = FormStartPosition.CenterParent;
            form_properties.labelProperties.Text = message;
            form_properties.ShowDialog();

        }

        private async void preferencesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var Settings = new FormPutioSettings();
            Settings.StartPosition = FormStartPosition.CenterParent;
            Settings.ShowDialog();

            InitializeControls();
            UpdateTreeView(await filemgr.List("0"), treeViewPutioFiles.SelectedNode);
        }

        private async void refreshToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            TreeNode node = treeViewPutioFiles.SelectedNode;

            var file = treeViewPutioFiles.SelectedNode.Tag as PutioFile;
            string id = file.id;
            UpdateTreeView(await filemgr.List(id), treeViewPutioFiles.SelectedNode);

        }

        private void renameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            treeViewPutioFiles.SelectedNode.BeginEdit();
        }

        private void autoDownloadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode selectedNode = treeViewPutioFiles.SelectedNode;
            var putiofile = selectedNode.Tag as PutioFile;
            if (putiofile.file_type == "FOLDER")
            {
                var frmAutoDownload = new FormPutioAutoDownload();
                frmAutoDownload.ShowDialog();
                TreeNode auto = treeViewAutoDownloads.Nodes.Add(putiofile.name);
                putiofile.autodownload_extensions = frmAutoDownload.extensions;
                putiofile.autodownload_minsize = (frmAutoDownload.minsize * 1024) * 1024;
                auto.Tag = putiofile;
                frmAutoDownload.Dispose();
            }
        }

        // AutoDownloads Context Menu

        private void removeToolStripMenuItem_Click(object sender, EventArgs e)
        {

            if (treeViewAutoDownloads.SelectedNode != null)
                treeViewAutoDownloads.SelectedNode.Remove();
        }

        // Transfers Context Menu

        private async void newToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Clipboard.GetText().ToString().StartsWith("magnet:"))
            {
                string url = Clipboard.GetText().ToString();
                var file = treeViewPutioFiles.SelectedNode.Tag as PutioFile;
                var option = MessageBox.Show(string.Format("Download {0} to {1}", magnet(url), file.name), "test", MessageBoxButtons.OKCancel);

                if (option == DialogResult.OK)
                {
                    await trfrmgr.Add(url, file.id);
                    GetTransfers();
                }
            }
            else
                MessageBox.Show("Copy a magnet link and try again", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);

        }

        private void refreshToolStripMenuItem_Click(object sender, EventArgs e)
        {
            GetTransfers();
        }

        private void cleanToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        // View - Menu Strip

        private void autoDownloadsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var toolStripMenuItem = (sender as ToolStripMenuItem);
            Properties.Settings.Default.ShowAutoDownloads = toolStripMenuItem.Checked;
            Properties.Settings.Default.Save();
            toolStripMenuItem.Checked = !toolStripMenuItem.Checked;
            splitContainerFiles.Panel2Collapsed = !toolStripMenuItem.Checked;
        }

        private void transfersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var toolStripMenuItem = (sender as ToolStripMenuItem);
            Properties.Settings.Default.ShowTransfers = toolStripMenuItem.Checked;
            Properties.Settings.Default.Save();
            toolStripMenuItem.Checked = !toolStripMenuItem.Checked;
        }

        private void managersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var toolStripMenuItem = (sender as ToolStripMenuItem);
            Properties.Settings.Default.ShowManager = toolStripMenuItem.Checked;
            Properties.Settings.Default.Save();
            toolStripMenuItem.Checked = !toolStripMenuItem.Checked;
            splitContainer1.Panel2Collapsed = !toolStripMenuItem.Checked;
        }

        // File - Menu strip

        private void closeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void openDownloadsFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string dirDownload = Properties.Settings.Default.DownloadDirectory;
            if (Directory.Exists(dirDownload))
                Process.Start(dirDownload);
        }

        // Tree View Putio Files

        private async void treeViewPutioFiles_AfterLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
            if (e.Label != null)
            {                
                var rename_response = await filemgr.Rename((e.Node.Tag as PutioFile).id, e.Label);
                (e.Node.Tag as PutioFile).name = e.Label;
            }
        }

        private async void treeViewPutioFiles_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            treeViewPutioFiles.SelectedNode = e.Node;
            PutioFile file = e.Node.Tag as PutioFile;

            if (e.Node != rootnode)
            {
                UpdateTreeView(await filemgr.List(file.id), e.Node);
            }
            
        }

        // Methods and Modules

        private async void DeleteFile(PutioFile inPutioFile)
        {
            var deletemessage = await filemgr.Delete(inPutioFile.id);
            string status = JObject.Parse(deletemessage)["status"].ToString();
            if (status == "OK")
            {
                if (inPutioFile.file != null)   
                    treeViewPutioFiles.Nodes.Remove(inPutioFile.file);
            }
        }

        private void ExportDataGridView()
        {
            var dt = new DataTable();
            dt = (DataTable)dataGridViewDownloads.DataSource;
            dt.TableName = "Download Queue";
            dt.WriteXml(Application.StartupPath + @"\DownloadQueue.xml", true);
        }

        private PutioFile SetPutioFile(JObject file)
        {
            var putiofile = new PutioFile(file["id"].ToString(), file["name"].ToString());
            putiofile.parent_id = file["parent_id"].ToString();
            putiofile.content_type = file["content_type"].ToString();
            putiofile.file_type = file["file_type"].ToString();
            putiofile.download_path = Properties.Settings.Default.DownloadDirectory + @"\" + putiofile.name;
            putiofile.size = file["size"].ToString();
            if (putiofile.content_type != "FOLDER")
                putiofile.downloadlink = new Uri(urlPutioApi + "files/" + putiofile.id + "/download?oauth_token=" + OAuthToken);

            return putiofile;
        }

        // test button!

        private async void testToolStripMenuItem_Click_1(object sender, EventArgs e)
        {
            var response = await trfrmgr.List();
            foreach (JObject jobject in response){
                Console.WriteLine(jobject);
            }
        }

        private void checkToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CheckAutoDownloads();
        }

        private void dataGridViewDownloads_CellClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void closeToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            closeform = true;
            Application.Exit();
        }

        private void menuStripMain_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {

        }

        private async void createFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode selectednode = treeViewPutioFiles.SelectedNode;
            var file = selectednode.Tag as PutioFile;
            string id = (selectednode.Tag as PutioFile).id;
            selectednode.Expand();
            if (file.file_type != "FOLDER")
            {
                id = file.parent_id;
                selectednode = selectednode.Parent;
            }
                
            var resposne = await filemgr.CreateFolder("New Folder", id);
            TreeNode node = selectednode.Nodes.Add("New Folder");
            node.Tag = SetPutioFile(resposne);
            var putiofile = node.Tag as PutioFile;
            putiofile.file = node;
            node.BeginEdit();
        } 

        private void contextMenuStripPutioFiles_Opening(object sender, CancelEventArgs e)
        {
            TreeNode selectednode = treeViewPutioFiles.SelectedNode;
            var file = selectednode.Tag as PutioFile;
            if (file.file_type == "FOLDER")
                contextMenuStripPutioFiles.Items["downloadToolStripMenuItem"].Enabled = false;

            if (selectednode == rootnode)
            {
                contextMenuStripPutioFiles.Items["downloadToolStripMenuItem"].Enabled = false;
            }
            else
                contextMenuStripPutioFiles.Enabled = true;

        }
    }
}
