// 
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DamnTabs
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void buttonBrowse_Click(object sender, EventArgs e)
        {
            using var d = new OpenFileDialog()
            {
                Filter = "Word Document (*.docx)|*.docx",
                ValidateNames = true
            };
            d.FileOk += openFileDialog_FileOk;
            d.ShowDialog();
        }

        private void openFileDialog_FileOk(object sender, CancelEventArgs e)
        {
            if (!e.Cancel)
            {
                textBoxFile.Text = ((OpenFileDialog)sender).FileName;
                textBoxFile.BackColor = System.Drawing.Color.White;
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            comboBoxMode.SelectedIndex = 0;
            toolStripProgressBar1.Visible = false;
            toolStripProgressBar1.Value = toolStripProgressBar1.Minimum;
        }

        private void Run()
        {
            // This is the path to the new file not the old one.
            var path = $"{Path.GetDirectoryName(textBoxFile.Text)}\\{Path.GetFileNameWithoutExtension(textBoxFile.Text)}-{comboBoxMode.Text}_DamnTabs.docx";

            toolStripStatusLabel1.Text = "Duplicating Document";
            // Dupe the file
            {
                int i = 0;
                while (File.Exists(path))
                {
                    i++;
                    path = $"{Path.GetDirectoryName(textBoxFile.Text)}\\{Path.GetFileNameWithoutExtension(textBoxFile.Text)}-{comboBoxMode.Text} {i}_DamnTabs.docx";
                }
                File.Copy(textBoxFile.Text, path);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            }


            // Get the Doc
            toolStripStatusLabel1.Text = "Opening...";
            using FileStream docx = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var wordDoc = WordprocessingDocument.Open(docx, true);
            var doc = wordDoc.MainDocumentPart.Document;

            // Fix the doc
            if (comboBoxMode.SelectedIndex == 0)
            {
                ReplaceTabs(doc);
            }
            else if (comboBoxMode.SelectedIndex == 1)
            {
                toolStripStatusLabel1.Text = "Scanning Document...";
                // Get all indent nodes
                var ps = from bodyChild in doc.Body
                         where bodyChild is Paragraph
                         let p = (Paragraph)bodyChild                       // WE MAKE NO ASSUMPTIONS:
                         where p.GetFirstChild<Run>() != null               // Checks if a run exists
                         && p.ParagraphProperties != null                   // Checks if w:pPr exists
                         && p.ParagraphProperties.Indentation != null       // Checks if w:ind exists
                         && p.ParagraphProperties.Indentation.FirstLine != null
                         select p;

                // Set up Progress Bar
                toolStripStatusLabel1.Text = "Running";
                var progMax = ps.Count();
                InitProgressBar(progMax > 0 ? progMax : 1);

                // Fix
                foreach (var p in ps)
                {
                    var firstRun = p.GetFirstChild<Run>();
                    var rpr = firstRun.GetFirstChild<RunProperties>();

                    if (rpr == null)
                        firstRun.FirstChild.InsertBeforeSelf(new TabChar());
                    else
                        rpr.InsertAfterSelf(new TabChar());
                    p.ParagraphProperties.Indentation.FirstLine = null;

                    toolStripProgressBar1.Value++;
                }
                toolStripProgressBar1.Value = toolStripProgressBar1.Maximum;
            }

            // SAVE FILE
            doc.Save();

            toolStripStatusLabel1.Text = "Done!";
        }

        private void ReplaceTabs(Document doc)
        {
            // TODO: Can we improve?

            var indTally = new Dictionary<string, int>(3);
            toolStripStatusLabel1.Text = "Scanning Document (1/2)";
            var inds = from bodyChild in doc.Body
                       where bodyChild is Paragraph
                       let p = (Paragraph)bodyChild                       // WE MAKE NO ASSUMPTIONS:
                       where p.ParagraphProperties != null                // Checks if w:pPr exists
                       && p.ParagraphProperties.Indentation != null       // Checks if w:ind exists
                       && p.ParagraphProperties.Indentation.FirstLine != null
                       select p.ParagraphProperties.Indentation.FirstLine;
            int indsCount = inds.Count();

            var badRuns = from bodyChild in doc.Body
                          where bodyChild is Paragraph
                          let p = (Paragraph)bodyChild
                          let r = p.GetFirstChild<Run>()
                          where r != null
                          let tab = r.GetFirstChild<TabChar>()
                          where tab != null
                          let t = r.GetFirstChild<Text>()
                          where t == null || t.IsAfter(tab)
                          select r;
            int badRunsCount = badRuns.Count();

            toolStripStatusLabel1.Text = "Scanning Document (2/2)";

            // Init Progress Bar
            InitProgressBar(indsCount + badRunsCount);

            foreach (var ind in inds)
            {
                if (!indTally.ContainsKey(ind.Value))
                    indTally.Add(ind.Value, 1);
                else
                    indTally[ind.Value]++;
                toolStripProgressBar1.Value++;
            }

            string indVal;
            {
                string IndVal()
                {
                    KeyValuePair<string, int> max = indTally.FirstOrDefault();
                    foreach (var kv in indTally)
                    {
                        if (max.Value < kv.Value)
                            max = kv;
                    }
                    return max.Key;
                };
                indVal = IndVal();
            }

            toolStripStatusLabel1.Text = "Running";
            foreach (var run in badRuns)
            {
                // Double Check if null and Remove tab
                var tab = run.GetFirstChild<TabChar>();
                if (tab != null) tab.Remove();

                var p = ((Paragraph)run.Parent);

                // Get/Create ParagraphProperties
                ParagraphProperties ppr;
                if (p.ParagraphProperties == null)
                {
                    ppr = new ParagraphProperties();
                    p.ParagraphProperties = ppr;
                }
                else ppr = p.ParagraphProperties;

                // Check if indentation is 
                if (ppr.Indentation == null)
                    ppr.Indentation = new Indentation();

                if (!string.IsNullOrWhiteSpace(indVal))
                    ppr.Indentation.FirstLine = indVal;
                else 
                    ppr.Indentation.FirstLine = null;
                toolStripProgressBar1.Value++;
            }
            if (string.IsNullOrWhiteSpace(indVal))
                MessageBox.Show(
                    "We removed the tabs, but we couldn't find any direct style indents to replace them with. You can set up your indentation by editing your document's styles.",
                    "Tab Removal",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
        }

        private void HideProgressBar()
        {
            toolStripProgressBar1.Value = toolStripProgressBar1.Minimum;
            toolStripProgressBar1.Visible = false;
        }

        private void InitProgressBar(int max)
        {
            // Set up Progress Bar
            toolStripProgressBar1.Minimum = 0;
            toolStripProgressBar1.Maximum = max;
            toolStripProgressBar1.Value = 0;
            toolStripProgressBar1.Visible = true;
        }

        private void buttonRun_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(textBoxFile.Text) && File.Exists(textBoxFile.Text))
            {
                Run();
            }
            else
            {
                textBoxFile.BackColor = System.Drawing.Color.LightCoral;
                toolStripStatusLabel1.Text = "Invalid Fields";
            }
        }

        private void ReadyUp(object sender, EventArgs e) => Ready();

        private void Ready()
        {
            if (toolStripStatusLabel1.Text != "Ready")
            {
                HideProgressBar();
                toolStripStatusLabel1.Text = "Ready";
            }
        }
    }
}
