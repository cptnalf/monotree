using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using System.IO;
using System.Threading;
using Microsoft.Glee.Drawing;
using Microsoft.Glee.GraphViewerGdi;

namespace Monotree
{
    /// <summary>Main window of Monotree.</summary>
    public partial class Main : Form
    {
        /// <summary>Database connection.</summary>
        Database database;

        /// <summary>Active revisions.</summary>
        Dictionary<string, Revision> revs;

        /// <summary>Container to assign the same colors to the same authors.</summary>
        Dictionary<string, System.Drawing.Color> colors = new Dictionary<string, System.Drawing.Color>();

        /// <summary>Active revision.</summary>
        /// <remarks>A revision is active when the user right-clicks on it.</remarks>
        Revision activeRev;

        /// <summary>Last position where mouse wheel was moved.</summary>
        System.Drawing.Point wheelPosition;

        /// <summary>Creates and shows the main window.</summary>
        public Main()
        {
            InitializeComponent();

            viewer.RemoveToolbar();
            viewer.OutsideAreaBrush = System.Drawing.Brushes.White;
            viewer.MouseWheel += new MouseEventHandler(viewer_MouseWheel);
            toolStripLimit.SelectedIndex = 3;
        }

        /// <summary>Event handler for mouse wheel events.</summary>
        void viewer_MouseWheel(object sender, MouseEventArgs e)
        {
            if (e.Location != wheelPosition)
            {
                if (e.Delta > 0)
                    viewer.ZoomInPressed();
                else
                    viewer.ZoomOutPressed();
                wheelPosition = e.Location;
            }
            else
            {
                wheelPosition = new System.Drawing.Point(-1, -1);
            }
        }

        /// <summary>Initializes the GUI.</summary>
        private void InitGUI()
        {
            viewer.PanButtonPressed = true;
            viewer.Visible = true;
            menuItemSave.Enabled = true;
            menuItemCompress.Enabled = true;
            toolStripRefresh.Enabled = true;
            toolStripZoomIn.Enabled = true;
            toolStripZoomOut.Enabled = true;
        }

        /// <summary>Event handler for clicks on menu buttons.</summary>
        private void MenuClick(object sender, EventArgs e)
        {
            if (sender == menuItemOpen)
            {
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    Text = "Monotree - " + Regex.Replace(openFileDialog.FileName, @"^.*\\", "");
                    database = new Database(openFileDialog.FileName);
                    List<string> names = database.GetBranchNames();
                    toolStripBranches.Items.Clear();
                    if (names.Count > 0)
                    {
                        foreach (string name in names)
                            toolStripBranches.Items.Add(name);
                        toolStripBranches.SelectedIndex = 0;
                        InitGUI();
                        toolStripRefresh.PerformClick();
                    }
                }
            }
            else if (sender == menuItemSave)
            {
                saveFileDialog.Title = "Save As";
                saveFileDialog.Filter = "PNG (*.png)|*.png";
                saveFileDialog.DefaultExt = ".png";
                saveFileDialog.FileName = Regex.Replace(database.FileName, @"\.[^.]*$", ".png");
                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    GraphRenderer renderer = new GraphRenderer(CreateGraph(revs));
                    renderer.CalculateLayout();
                    System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap((int)viewer.GraphWidth, (int)viewer.GraphHeight);
                    renderer.Render(bitmap);
                    bitmap.Save(saveFileDialog.FileName, System.Drawing.Imaging.ImageFormat.Png);
                    toolStripProgressBar.Visible = false;
                }
            }
            else if (sender == menuItemExit)
            {
                Close();
            }
            else if (sender == menuItemStatusBar)
            {
                statusStripMain.Visible = menuItemStatusBar.Checked;
            }
            else if (sender == menuItemCompress)
            {
                saveFileDialog.Title = "Compress";
                saveFileDialog.Filter = "MTN (*.mtn)|*.mtn";
                saveFileDialog.DefaultExt = ".mtn";
                saveFileDialog.FileName = Regex.Replace(database.FileName, @"\.[^.]*$", ".mtn");
                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    if (database.FileName.Equals(saveFileDialog.FileName))
                    {
                        MessageBox.Show("The database is currently open and can't be overwritten.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else
                    {
                        Thread thread = new Thread(delegate()
                        {
                            File.Copy(database.FileName, saveFileDialog.FileName);
                            Database db = new Database(saveFileDialog.FileName);
                            db.Compress();
                        });
                        thread.Start();
                    }
                }
            }
            else if (sender == menuItemOptions)
            {
                Options options = new Options();
                options.Pages.Add(new GeneralOptions());
                options.Pages.Add(new DisplayOptions());
                if (options.ShowDialog() == DialogResult.OK)
                    colors.Clear();
            }
            else if (sender == menuItemAbout)
            {
                About about = new About();
                about.ShowDialog();
            }
            else if (sender == ctxMenuItemCopyRevisionID)
            {
                if (activeRev != null)
                    Clipboard.SetText(activeRev.ID);
            }
        }

        /// <summary>Event handler for clicks on toolbar buttons.</summary>
        private void ToolbarClick(object sender, EventArgs e)
        {
            if (sender == toolStripRefresh)
            {
                revs = database.GetLatestRevisionIDs(toolStripBranches.SelectedItem.ToString(), Int32.Parse(toolStripLimit.SelectedItem.ToString().Substring(0, toolStripLimit.SelectedItem.ToString().IndexOf(' '))));
                if (revs.Count < 2)
                {
                    MessageBox.Show("The branch " + toolStripBranches.SelectedItem.ToString() + " can not be displayed as it contains only one revision.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    Graph graph = CreateGraph(revs);
                    viewer.Graph = graph;
                    viewer.ZoomF = Math.Max(graph.Width / viewer.Width, graph.Height / viewer.Height);
                    toolStripProgressBar.Visible = false;
                    viewer.Focus();
                }
            }
            else if (sender == toolStripZoomIn)
            {
                viewer.ZoomInPressed();
            }
            else if (sender == toolStripZoomOut)
            {
                viewer.ZoomOutPressed();
            }
        }

        /// <summary>Creates the graph for the current branch.</summary>
        /// <param name="revs">Revisions to create graph for.</param>
        /// <returns>Graph.</returns>
        private Microsoft.Glee.Drawing.Graph CreateGraph(Dictionary<string, Revision> revs)
        {
            toolStripProgressBar.Visible = true;
            toolStripProgressBar.Value = 0;
            toolStripProgressBar.Maximum = revs.Count;

            Graph graph = new Graph(database.FileName);
            graph.GraphAttr.NodeAttr.LineWidth = 2;
            graph.GraphAttr.NodeAttr.FontName = "Verdana, Arial, Helvetica, sans-serif";
            graph.GraphAttr.NodeAttr.Fontsize = 8;
            graph.GraphAttr.NodeAttr.XRad = 8;
            graph.GraphAttr.NodeAttr.YRad = 8;
            graph.GraphAttr.EdgeAttr.LineWidth = 2;
            foreach (KeyValuePair<string, Revision> rev in revs)
            {
                foreach (string parent in rev.Value.Parents)
                {
                    if (revs.ContainsKey(parent))
                    {
                        Edge edge = (Edge)graph.AddEdge(parent, rev.Key);
                        edge.Attr.Color = Microsoft.Glee.Drawing.Color.Black;
                        Node node = (Node)graph.FindNode(parent);
                        node.UserData = revs[parent];
                        FormatNode(node, revs[parent]);
                        node = (Node)graph.FindNode(rev.Key);
                        node.UserData = rev.Value;
                        FormatNode(node, rev.Value);
                    }
                    else
                    {
                        Revision parentRev = database.GetRevision(parent);
                        if (parentRev != null && parentRev.Branch == rev.Value.Branch)
                        {
                            Edge edge = (Edge)graph.AddEdge(parent, rev.Key);
                            edge.Attr.Color = Microsoft.Glee.Drawing.Color.Black;
                            Node node = (Node)graph.FindNode(parent);
                            node.UserData = parentRev;
                            FormatNode(node, parentRev);
                            node = (Node)graph.FindNode(rev.Key);
                            node.UserData = rev.Value;
                            FormatNode(node, rev.Value);
                        }
                        else
                        {
                            Edge edge = (Edge)graph.AddEdge(parent, rev.Key);
                            edge.Attr.Color = Microsoft.Glee.Drawing.Color.Black;
                            edge.Attr.AddStyle(Style.Dashed);
                            Node node = (Node)graph.FindNode(parent);
                            node.UserData = database.GetRevision(parent);
                            FormatNodeFromDifferentBranch(node, (Revision)node.UserData);
                            node = (Node)graph.FindNode(rev.Key);
                            node.UserData = rev.Value;
                            FormatNode(node, rev.Value);
                        }
                    }
                }
                toolStripProgressBar.PerformStep();
            }

            toolStripProgressBar.Value = toolStripProgressBar.Maximum;
            return graph;
        }

        /// <summary>Gets the color for the specified author.</summary>
        /// <param name="author">Author to get the color for.</param>
        /// <returns>Color.</returns>
        private Microsoft.Glee.Drawing.Color GetColor(string author)
        {
            System.Drawing.Color color;
            if (!colors.TryGetValue(author, out color))
            {
                StringCollection bgColors = (StringCollection)Properties.Settings.Default["RevisionBgColors"];
                if (colors.Count < bgColors.Count)
                {
                    color = System.Drawing.Color.FromArgb(Int32.Parse(bgColors[colors.Count], System.Globalization.NumberStyles.HexNumber));
                    colors.Add(author, color);
                }
                else
                {
                    color = System.Drawing.Color.Gold;
                }
            }
            return new Microsoft.Glee.Drawing.Color(color.R, color.G, color.B);
        }

        /// <summary>Formats a node.</summary>
        /// <param name="node">Node to format.</param>
        /// <param name="rev">Revision which is represented by the node.</param>
        private void FormatNode(Node node, Revision rev)
        {
            node.Attr.Fontcolor = Microsoft.Glee.Drawing.Color.White;
            node.Attr.Fillcolor = GetColor(rev.Author);
            if (rev.Parents.Count > 1)
            {
                node.Attr.LabelMargin = 10;
                node.Attr.Label = rev.ID.Substring(0, Math.Min(20, rev.ID.Length));
                node.Attr.Shape = Shape.Parallelogram;
            }
            else
            {
                node.Attr.LabelMargin = 5;
								node.Attr.Label = rev.ID /*.Substring(0, Math.Min(20, rev.ID.Length))*/ + "\n"  + rev.Author + "\n";
                if ((bool)Properties.Settings.Default["ToLocalTime"])
									node.Attr.Label += rev.Date.ToLocalTime().ToString();
                else
									node.Attr.Label += rev.Date.ToString();
                node.Attr.Shape = Shape.Box;
            }
        }

        /// <summary>Formats a node from a different branch.</summary>
        /// <param name="node">Node to format.</param>
        /// <param name="rev">Revision which is represented by the node.</param>
        private void FormatNodeFromDifferentBranch(Node node, Revision rev)
        {
            node.Attr.Fontcolor = Microsoft.Glee.Drawing.Color.Black;//Microsoft.Glee.Drawing.Color.White;
            node.Attr.Fillcolor = Microsoft.Glee.Drawing.Color.LightGray;
            node.Attr.LabelMargin = 5;
						node.Attr.Label = rev.Branch + "\n" +rev.ID /*.Substring(0, Math.Min(20, rev.Branch.Length))*/ + "\n" + rev.Author + "\n";
						
            if ((bool)Properties.Settings.Default["ToLocalTime"])
							node.Attr.Label += rev.Date.ToLocalTime().ToString();
            else
							node.Attr.Label += rev.Date.ToString();
            node.Attr.Shape = Shape.Box;
        }

        /// <summary>Event handler called when a new shape is selected in the viewer.</summary>
        private void viewer_SelectionChanged(object sender, EventArgs e)
        {
            object selected = viewer.SelectedObject;
            if (selected == null)
            {
                viewer.SetToolTip(toolTip, "");
            }
            else if (selected is Node)
            {
                Revision rev = (Revision)((Node)selected).UserData;
                viewer.SetToolTip(toolTip, rev.Log);
            }
        }

        /// <summary>Event handlers for mouse clicks in the viewer.</summary>
        private void viewer_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                object clicked = viewer.GetObjectAt(e.X, e.Y);
                if (clicked is Node)
                    Clipboard.SetText(((Revision)((Node)clicked).UserData).ID);
            }
        }

        /// <summary>Event handler called when context menu is opened in the viewer.</summary>
        private void contextMenuStripViewer_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Point p = viewer.PointToClient(Cursor.Position);
            Node current = viewer.GetObjectAt(p.X, p.Y) as Node;
            activeRev = (current != null) ? (Revision)current.UserData : null;
            ctxMenuItemCopyRevisionID.Enabled = (activeRev != null);
        }
    }
}
