Imports System.Runtime.CompilerServices
Imports System.Runtime.InteropServices

Module UIHelper
    Private Const EM_SETCUEBANNER As Integer = &H1501
    Private Const EM_SETMARGINS As Integer = &HD3
    Private Const EC_LEFTMARGIN As Integer = &H1
    Private Const EC_RIGHTMARGIN As Integer = &H2

    <DllImport("user32.dll", CharSet:=CharSet.Unicode, SetLastError:=False)>
    Private Function SendMessage(hWnd As IntPtr, Msg As Integer, wParam As IntPtr, lParam As String) As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=False)>
    Private Function SendMessage(hWnd As IntPtr, Msg As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function

    <Extension()>
    Public Sub SetPlaceholder(tb As TextBox, placeholder As String, Optional showWhenFocused As Boolean = False)
        Dim fDrawFocused As IntPtr = If(showWhenFocused, CType(1, IntPtr), IntPtr.Zero)
        SendMessage(tb.Handle, EM_SETCUEBANNER, fDrawFocused, placeholder)
    End Sub

    <Extension()>
    Public Sub AddButtonOverlay(tb As TextBox, buttonText As String, handler As EventHandler)
        If tb Is Nothing Then Return
        Dim parent = tb.Parent
        If parent Is Nothing Then Return

        Dim btn As New Button()
        btn.Text = buttonText
        btn.FlatStyle = FlatStyle.Standard
        btn.FlatAppearance.BorderSize = 1
        btn.Cursor = Cursors.Hand
        btn.TabStop = False

        ' Size: use textbox height for square button, limit width
        Dim btnHeight = Math.Max(16, tb.Height - 4)
        Dim btnWidth = Math.Max(24, Math.Min(80, CInt(btnHeight * 1.5)))
        btn.Size = New Size(btnWidth, btnHeight)

        parent.Controls.Add(btn)
        btn.BringToFront()

        Dim reposition = Sub()
                             Dim x = tb.Left + tb.Width - btn.Width - 2
                             Dim y = tb.Top + CInt((tb.Height - btn.Height) / 2)
                             btn.Location = New Drawing.Point(x, y)
                             ' set right margin so text isn't hidden
                             Dim rightMargin = btn.Width + 4
                             Dim lParam As IntPtr = New IntPtr((rightMargin << 16) And &HFFFFFFFF)
                             SendMessage(tb.Handle, EM_SETMARGINS, New IntPtr(EC_RIGHTMARGIN), lParam)
                         End Sub

        ' initial position
        reposition()

        AddHandler btn.Click, handler

        ' update when textbox moves or resizes
        Dim locationHandler As EventHandler = Sub(s, e) reposition()
        AddHandler tb.LocationChanged, locationHandler
        AddHandler tb.SizeChanged, locationHandler

        ' also when parent resizes
        Dim parentHandler As EventHandler = Sub(s, e) reposition()
        AddHandler parent.SizeChanged, parentHandler

        ' cleanup when textbox disposed
        AddHandler tb.Disposed, Sub(s, e)
                                    Try
                                        RemoveHandler tb.LocationChanged, locationHandler
                                        RemoveHandler tb.SizeChanged, locationHandler
                                        RemoveHandler parent.SizeChanged, parentHandler
                                        RemoveHandler btn.Click, handler
                                        If btn IsNot Nothing AndAlso Not btn.IsDisposed Then
                                            parent.Controls.Remove(btn)
                                            btn.Dispose()
                                        End If
                                    Catch
                                    End Try
                                End Sub
    End Sub
End Module
