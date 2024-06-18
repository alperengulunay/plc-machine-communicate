namespace ERN028_MakinaVeriTopalamaFormsApp
{
    partial class Form1
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
            this.components = new System.ComponentModel.Container();
            this.btnJson = new System.Windows.Forms.Button();
            this.progressBar1 = new System.Windows.Forms.ProgressBar();
            this.timer1 = new System.Windows.Forms.Timer(this.components);
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.btnReadData = new System.Windows.Forms.Button();
            this.textBox2 = new System.Windows.Forms.TextBox();
            this.SuspendLayout();
            // 
            // btnJson
            // 
            this.btnJson.Location = new System.Drawing.Point(437, 12);
            this.btnJson.Name = "btnJson";
            this.btnJson.Size = new System.Drawing.Size(271, 65);
            this.btnJson.TabIndex = 0;
            this.btnJson.Text = "Grafik Testi Başlat";
            this.btnJson.UseVisualStyleBackColor = true;
            this.btnJson.Click += new System.EventHandler(this.btnJson_Click);
            // 
            // progressBar1
            // 
            this.progressBar1.Location = new System.Drawing.Point(-9, 91);
            this.progressBar1.Name = "progressBar1";
            this.progressBar1.Size = new System.Drawing.Size(817, 10);
            this.progressBar1.TabIndex = 1;
            // 
            // textBox1
            // 
            this.textBox1.Location = new System.Drawing.Point(12, 12);
            this.textBox1.Name = "textBox1";
            this.textBox1.Size = new System.Drawing.Size(419, 22);
            this.textBox1.TabIndex = 4;
            // 
            // btnReadData
            // 
            this.btnReadData.Location = new System.Drawing.Point(325, 40);
            this.btnReadData.Name = "btnReadData";
            this.btnReadData.Size = new System.Drawing.Size(106, 23);
            this.btnReadData.TabIndex = 5;
            this.btnReadData.Text = "Read Data";
            this.btnReadData.UseVisualStyleBackColor = true;
            this.btnReadData.Click += new System.EventHandler(this.btnReadData_Click_1);
            // 
            // textBox2
            // 
            this.textBox2.Location = new System.Drawing.Point(12, 41);
            this.textBox2.Name = "textBox2";
            this.textBox2.Size = new System.Drawing.Size(290, 22);
            this.textBox2.TabIndex = 6;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(720, 101);
            this.Controls.Add(this.textBox2);
            this.Controls.Add(this.btnReadData);
            this.Controls.Add(this.textBox1);
            this.Controls.Add(this.progressBar1);
            this.Controls.Add(this.btnJson);
            this.Name = "Form1";
            this.Text = "Form1";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnJson;
        private System.Windows.Forms.ProgressBar progressBar1;
        private System.Windows.Forms.Timer timer1;
        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.Button btnReadData;
        private System.Windows.Forms.TextBox textBox2;
    }
}

