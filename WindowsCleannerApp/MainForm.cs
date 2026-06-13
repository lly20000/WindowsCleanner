using System.ComponentModel;

namespace WindowsCleannerApp;

internal sealed class MainForm : Form
{
    private readonly FileSystemCleaner cleaner = new();
    private readonly BindingList<CleanupTargetViewModel> rows = [];
    private readonly Label summaryLabel = new();
    private readonly Label statusLabel = new();
    private readonly DataGridView grid = new();
    private readonly Button scanButton = new();
    private readonly Button cleanButton = new();

    public MainForm()
    {
        Text = "Windows Cleaner";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(940, 560);
        Size = new Size(1080, 640);
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);

        InitializeLayout();
        Load += async (_, _) => await ScanAsync();
    }

    private void InitializeLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18),
            BackColor = Color.FromArgb(245, 247, 250)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

        var titlePanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(28, 58, 96),
            Margin = new Padding(0, 0, 0, 14)
        };

        var titleLabel = new Label
        {
            Text = "Windows Cleanup Dashboard",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold, GraphicsUnit.Point),
            AutoSize = true,
            Location = new Point(22, 16)
        };

        var subtitleLabel = new Label
        {
            Text = "Scan temp files and update caches, review the estimated savings, then clean only what you select.",
            ForeColor = Color.FromArgb(217, 225, 235),
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            AutoSize = true,
            Location = new Point(24, 54)
        };

        titlePanel.Controls.Add(titleLabel);
        titlePanel.Controls.Add(subtitleLabel);

        var actionsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 12)
        };

        scanButton.Text = "Scan Again";
        scanButton.AutoSize = true;
        scanButton.Padding = new Padding(14, 8, 14, 8);
        scanButton.FlatStyle = FlatStyle.System;
        scanButton.Click += async (_, _) => await ScanAsync();

        cleanButton.Text = "Clean Selected";
        cleanButton.AutoSize = true;
        cleanButton.Padding = new Padding(14, 8, 14, 8);
        cleanButton.FlatStyle = FlatStyle.System;
        cleanButton.Click += async (_, _) => await CleanAsync();

        summaryLabel.AutoSize = true;
        summaryLabel.Margin = new Padding(18, 12, 0, 0);
        summaryLabel.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point);

        actionsPanel.Controls.Add(scanButton);
        actionsPanel.Controls.Add(cleanButton);
        actionsPanel.Controls.Add(summaryLabel);

        grid.Dock = DockStyle.Fill;
        grid.AutoGenerateColumns = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.None;
        grid.DataSource = rows;
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty)
            {
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                UpdateSummary();
            }
        };

        grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(CleanupTargetViewModel.Selected),
            HeaderText = "Clean",
            Width = 60
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(CleanupTargetViewModel.Name),
            HeaderText = "Target",
            Width = 180,
            ReadOnly = true
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(CleanupTargetViewModel.Location),
            HeaderText = "Path",
            Width = 320,
            ReadOnly = true
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(CleanupTargetViewModel.FileCount),
            HeaderText = "Files",
            Width = 80,
            ReadOnly = true
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(CleanupTargetViewModel.SizeText),
            HeaderText = "Space",
            Width = 110,
            ReadOnly = true
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(CleanupTargetViewModel.Status),
            HeaderText = "Status",
            Width = 110,
            ReadOnly = true
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(CleanupTargetViewModel.WarningText),
            HeaderText = "Notes",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            ReadOnly = true
        });

        var footerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 12, 0, 0)
        };

        statusLabel.AutoSize = true;
        statusLabel.Text = "Ready";
        statusLabel.ForeColor = Color.FromArgb(77, 92, 107);
        statusLabel.Location = new Point(0, 12);
        footerPanel.Controls.Add(statusLabel);

        root.Controls.Add(titlePanel, 0, 0);
        root.Controls.Add(actionsPanel, 0, 1);
        root.Controls.Add(grid, 0, 2);
        root.Controls.Add(footerPanel, 0, 3);

        Controls.Add(root);
    }

    private async Task ScanAsync()
    {
        SetBusyState(true, "Scanning common cleanup locations...");

        try
        {
            var targets = CleanupTargets.GetDefaultTargets();
            var scanResults = await Task.Run(() => cleaner.ScanTargets(targets));

            rows.RaiseListChangedEvents = false;
            rows.Clear();

            foreach (var scan in scanResults)
            {
                rows.Add(new CleanupTargetViewModel
                {
                    Target = scan.Target,
                    Selected = scan.Exists && scan.EstimatedBytes > 0,
                    Exists = scan.Exists,
                    FileCount = scan.FileCount,
                    EstimatedBytes = scan.EstimatedBytes,
                    Status = scan.Exists ? "Ready" : "Missing",
                    WarningText = string.Join(" | ", scan.Warnings)
                });
            }

            rows.RaiseListChangedEvents = true;
            rows.ResetBindings();
            UpdateSummary();
            statusLabel.Text = "Scan completed";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Scan failed";
            MessageBox.Show(this, ex.Message, "Scan failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private async Task CleanAsync()
    {
        var selectedRows = rows.Where(row => row.Selected && row.Exists && row.EstimatedBytes > 0).ToList();
        if (selectedRows.Count == 0)
        {
            MessageBox.Show(this, "Select at least one cleanup target first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selectedBytes = selectedRows.Sum(item => item.EstimatedBytes);
        var confirm = MessageBox.Show(
            this,
            $"Clean {selectedRows.Count} selected targets and free about {SizeFormatter.FormatBytes(selectedBytes)}?",
            "Confirm cleanup",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        SetBusyState(true, "Deleting selected files...");

        try
        {
            var selectedTargets = selectedRows.Select(row => row.Target).ToList();
            var freshScans = await Task.Run(() => cleaner.ScanTargets(selectedTargets));
            var cleanupResults = await Task.Run(() => cleaner.CleanTargets(freshScans));

            var releasedBytes = cleanupResults.Sum(item => item.DeletedBytes);
            var errorMessages = cleanupResults.SelectMany(item => item.Errors).Take(8).ToList();

            await ScanAsync();

            var resultMessage = $"Cleanup completed. Freed {SizeFormatter.FormatBytes(releasedBytes)}.";
            if (errorMessages.Count > 0)
            {
                resultMessage += Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, errorMessages);
            }

            MessageBox.Show(this, resultMessage, "Cleanup completed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            statusLabel.Text = "Cleanup completed";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Cleanup failed";
            MessageBox.Show(this, ex.Message, "Cleanup failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private void SetBusyState(bool busy, string? message = null)
    {
        scanButton.Enabled = !busy;
        cleanButton.Enabled = !busy;
        grid.Enabled = !busy;
        statusLabel.Text = message ?? "Ready";
        UseWaitCursor = busy;
    }

    private void UpdateSummary()
    {
        var totalBytes = rows.Where(row => row.Exists).Sum(row => row.EstimatedBytes);
        var selectedBytes = rows.Where(row => row.Selected && row.Exists).Sum(row => row.EstimatedBytes);
        summaryLabel.Text = $"Total {SizeFormatter.FormatBytes(totalBytes)}   Selected {SizeFormatter.FormatBytes(selectedBytes)}";
    }
}
