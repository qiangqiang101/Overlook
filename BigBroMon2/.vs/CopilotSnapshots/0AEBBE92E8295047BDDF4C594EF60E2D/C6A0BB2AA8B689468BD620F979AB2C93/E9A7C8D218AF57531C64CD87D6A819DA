Imports System.Runtime.InteropServices
Imports System.Runtime.CompilerServices
Imports System.Linq

Public Class frmMain
    Private Sub frmMain_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        txtSearch.SetPlaceholder("Search...")
        txtSearch.AddButtonOverlay("⌕", AddressOf OnSearchButtonClick)
        AddHandler txtSearch.KeyDown, Sub(s, ev)
                                          If ev.KeyCode = Keys.Enter AndAlso s.Focused Then
                                              ev.SuppressKeyPress = True
                                              OnSearchButtonClick(s, EventArgs.Empty)
                                          End If
                                      End Sub
    End Sub

    Private Sub OnSearchButtonClick(sender As Object, e As EventArgs)
        MessageBox.Show($"Search for: {txtSearch.Text}", "Search", MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub
End Class
