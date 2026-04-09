<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class frmMain
    Inherits System.Windows.Forms.Form

    'Form overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    'Required by the Windows Form Designer
    Private components As System.ComponentModel.IContainer

    'NOTE: The following procedure is required by the Windows Form Designer
    'It can be modified using the Windows Form Designer.
    'Do not modify it using the code editor.
    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        components = New ComponentModel.Container()
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(frmMain))
        tcMainTab = New TabControl()
        tpLibrary = New TabPage()
        tpGallery = New TabPage()
        tpUpdates = New TabPage()
        imgForTcTab = New ImageList(components)
        txtSearch = New TextBox()
        tmLHM = New Timer(components)
        tcMainTab.SuspendLayout()
        SuspendLayout()
        ' 
        ' tcMainTab
        ' 
        tcMainTab.Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        tcMainTab.Controls.Add(tpLibrary)
        tcMainTab.Controls.Add(tpGallery)
        tcMainTab.Controls.Add(tpUpdates)
        tcMainTab.ImageList = imgForTcTab
        tcMainTab.ItemSize = New Size(90, 35)
        tcMainTab.Location = New Point(13, 46)
        tcMainTab.Margin = New Padding(4)
        tcMainTab.Name = "tcMainTab"
        tcMainTab.SelectedIndex = 0
        tcMainTab.Size = New Size(758, 502)
        tcMainTab.TabIndex = 1000
        ' 
        ' tpLibrary
        ' 
        tpLibrary.ImageKey = "grip-solid.png"
        tpLibrary.Location = New Point(4, 39)
        tpLibrary.Margin = New Padding(4)
        tpLibrary.Name = "tpLibrary"
        tpLibrary.Padding = New Padding(4)
        tpLibrary.Size = New Size(750, 459)
        tpLibrary.TabIndex = 0
        tpLibrary.Text = "Library"
        tpLibrary.UseVisualStyleBackColor = True
        ' 
        ' tpGallery
        ' 
        tpGallery.ImageKey = "basket-shopping-solid.png"
        tpGallery.Location = New Point(4, 39)
        tpGallery.Margin = New Padding(4)
        tpGallery.Name = "tpGallery"
        tpGallery.Padding = New Padding(4)
        tpGallery.Size = New Size(750, 459)
        tpGallery.TabIndex = 1
        tpGallery.Text = "Gallery"
        tpGallery.UseVisualStyleBackColor = True
        ' 
        ' tpUpdates
        ' 
        tpUpdates.ImageKey = "arrow-rotate-right-solid.png"
        tpUpdates.Location = New Point(4, 39)
        tpUpdates.Margin = New Padding(4)
        tpUpdates.Name = "tpUpdates"
        tpUpdates.Size = New Size(750, 459)
        tpUpdates.TabIndex = 2
        tpUpdates.Text = "Updates"
        tpUpdates.UseVisualStyleBackColor = True
        ' 
        ' imgForTcTab
        ' 
        imgForTcTab.ColorDepth = ColorDepth.Depth32Bit
        imgForTcTab.ImageStream = CType(resources.GetObject("imgForTcTab.ImageStream"), ImageListStreamer)
        imgForTcTab.TransparentColor = Color.Transparent
        imgForTcTab.Images.SetKeyName(0, "arrow-rotate-right-solid.png")
        imgForTcTab.Images.SetKeyName(1, "basket-shopping-solid.png")
        imgForTcTab.Images.SetKeyName(2, "grip-solid.png")
        ' 
        ' txtSearch
        ' 
        txtSearch.Anchor = AnchorStyles.Top
        txtSearch.Location = New Point(244, 12)
        txtSearch.Name = "txtSearch"
        txtSearch.Size = New Size(295, 27)
        txtSearch.TabIndex = 1001
        ' 
        ' tmLHM
        ' 
        tmLHM.Enabled = True
        tmLHM.Interval = 10000
        ' 
        ' frmMain
        ' 
        AutoScaleDimensions = New SizeF(8F, 20F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(784, 561)
        Controls.Add(txtSearch)
        Controls.Add(tcMainTab)
        Font = New Font("Segoe UI", 11F)
        Margin = New Padding(4)
        Name = "frmMain"
        StartPosition = FormStartPosition.CenterScreen
        Text = "Big Bro Monitor 2"
        tcMainTab.ResumeLayout(False)
        ResumeLayout(False)
        PerformLayout()
    End Sub
    Friend WithEvents tcMainTab As TabControl
    Friend WithEvents tpLibrary As TabPage
    Friend WithEvents tpGallery As TabPage
    Friend WithEvents tpUpdates As TabPage
    Friend WithEvents imgForTcTab As ImageList
    Friend WithEvents txtSearch As TextBox
    Friend WithEvents tmLHM As Timer

End Class
