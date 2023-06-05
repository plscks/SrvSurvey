﻿namespace SrvSurvey
{
    partial class FormAllRuins
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            grid = new ListView();
            colSiteId = new ColumnHeader();
            colSystem = new ColumnHeader();
            colBody = new ColumnHeader();
            colDistance = new ColumnHeader();
            colArrival = new ColumnHeader();
            colSiteType = new ColumnHeader();
            colIndex = new ColumnHeader();
            colLastVisited = new ColumnHeader();
            colHasImages = new ColumnHeader();
            colSiteHeading = new ColumnHeader();
            colRelicTowerHeading = new ColumnHeader();
            btnFilter = new Button();
            txtFilter = new TextBox();
            checkVisited = new CheckBox();
            comboSiteType = new ComboBox();
            label1 = new Label();
            comboCurrentSystem = new ComboBox();
            SuspendLayout();
            // 
            // grid
            // 
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            grid.Columns.AddRange(new ColumnHeader[] { colSiteId, colSystem, colBody, colDistance, colArrival, colSiteType, colIndex, colLastVisited, colHasImages, colSiteHeading, colRelicTowerHeading });
            grid.FullRowSelect = true;
            grid.GridLines = true;
            grid.Location = new Point(12, 40);
            grid.Name = "grid";
            grid.ShowGroups = false;
            grid.Size = new Size(860, 377);
            grid.TabIndex = 0;
            grid.UseCompatibleStateImageBehavior = false;
            grid.View = View.Details;
            grid.ColumnClick += grid_ColumnClick;
            grid.DoubleClick += grid_DoubleClick;
            grid.MouseClick += grid_MouseClick;
            // 
            // colSiteId
            // 
            colSiteId.Text = "ID";
            colSiteId.TextAlign = HorizontalAlignment.Right;
            // 
            // colSystem
            // 
            colSystem.Text = "System";
            colSystem.Width = 110;
            // 
            // colBody
            // 
            colBody.Text = "Body";
            colBody.Width = 110;
            // 
            // colDistance
            // 
            colDistance.Text = "System distance";
            colDistance.TextAlign = HorizontalAlignment.Right;
            // 
            // colArrival
            // 
            colArrival.Text = "Arrival distance:";
            colArrival.TextAlign = HorizontalAlignment.Right;
            // 
            // colSiteType
            // 
            colSiteType.Text = "Site type";
            colSiteType.TextAlign = HorizontalAlignment.Center;
            colSiteType.Width = 90;
            // 
            // colIndex
            // 
            colIndex.Text = "Ruins #";
            colIndex.TextAlign = HorizontalAlignment.Center;
            // 
            // colLastVisited
            // 
            colLastVisited.Text = "Last visited";
            colLastVisited.TextAlign = HorizontalAlignment.Center;
            colLastVisited.Width = 90;
            // 
            // colHasImages
            // 
            colHasImages.Text = "Has images";
            colHasImages.TextAlign = HorizontalAlignment.Center;
            colHasImages.Width = 80;
            // 
            // colSiteHeading
            // 
            colSiteHeading.Text = "Site heading";
            colSiteHeading.TextAlign = HorizontalAlignment.Center;
            colSiteHeading.Width = 90;
            // 
            // colRelicTowerHeading
            // 
            colRelicTowerHeading.Text = "Relic tower heading";
            colRelicTowerHeading.TextAlign = HorizontalAlignment.Center;
            colRelicTowerHeading.Width = 130;
            // 
            // btnFilter
            // 
            btnFilter.Location = new Point(12, 12);
            btnFilter.Name = "btnFilter";
            btnFilter.Size = new Size(50, 23);
            btnFilter.TabIndex = 1;
            btnFilter.Text = "Filter";
            btnFilter.UseVisualStyleBackColor = true;
            btnFilter.Click += btnFilter_Click;
            // 
            // txtFilter
            // 
            txtFilter.Location = new Point(68, 12);
            txtFilter.Name = "txtFilter";
            txtFilter.Size = new Size(254, 23);
            txtFilter.TabIndex = 2;
            // 
            // checkVisited
            // 
            checkVisited.AutoSize = true;
            checkVisited.Location = new Point(328, 14);
            checkVisited.Name = "checkVisited";
            checkVisited.Size = new Size(88, 19);
            checkVisited.TabIndex = 3;
            checkVisited.Text = "Only visited";
            checkVisited.UseVisualStyleBackColor = true;
            checkVisited.CheckedChanged += checkVisited_CheckedChanged;
            // 
            // comboSiteType
            // 
            comboSiteType.DropDownStyle = ComboBoxStyle.DropDownList;
            comboSiteType.FormattingEnabled = true;
            comboSiteType.Items.AddRange(new object[] { "All types", "Alpha", "Beta", "Gamma" });
            comboSiteType.Location = new Point(422, 12);
            comboSiteType.Name = "comboSiteType";
            comboSiteType.Size = new Size(121, 23);
            comboSiteType.TabIndex = 4;
            comboSiteType.SelectedIndexChanged += comboSiteType_SelectedIndexChanged;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(549, 15);
            label1.Name = "label1";
            label1.Size = new Size(136, 15);
            label1.TabIndex = 5;
            label1.Text = "Measure distances from:";
            // 
            // comboCurrentSystem
            // 
            comboCurrentSystem.FormattingEnabled = true;
            comboCurrentSystem.Location = new Point(691, 11);
            comboCurrentSystem.Name = "comboCurrentSystem";
            comboCurrentSystem.Size = new Size(181, 23);
            comboCurrentSystem.TabIndex = 7;
            // 
            // FormAllRuins
            // 
            AcceptButton = btnFilter;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(884, 429);
            Controls.Add(comboCurrentSystem);
            Controls.Add(label1);
            Controls.Add(comboSiteType);
            Controls.Add(checkVisited);
            Controls.Add(txtFilter);
            Controls.Add(btnFilter);
            Controls.Add(grid);
            MinimumSize = new Size(900, 400);
            Name = "FormAllRuins";
            Text = "Guardian Ruins";
            Load += FormAllRuins_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private ListView grid;
        private ColumnHeader colSystem;
        private ColumnHeader colBody;
        private ColumnHeader colSiteType;
        private ColumnHeader colLastVisited;
        private ColumnHeader colSiteHeading;
        private ColumnHeader colRelicTowerHeading;
        private ColumnHeader colHasImages;
        private Button btnFilter;
        private ColumnHeader colDistance;
        private ColumnHeader colArrival;
        private TextBox txtFilter;
        private CheckBox checkVisited;
        private ComboBox comboSiteType;
        private ColumnHeader colIndex;
        private Label label1;
        private ColumnHeader colSiteId;
        private ComboBox comboCurrentSystem;
    }
}