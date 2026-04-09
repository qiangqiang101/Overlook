Imports System.Net.Http
Imports System.Text.Json

Public Class LHM

    Public Property id As Integer
    Public Property Text As String
    Public Property Min As String
    Public Property Value As String
    Public Property Max As String
    Public Property SensorId As String
    Public Property Type As String
    Public Property RawMin As String
    Public Property RawValue As String
    Public Property RawMax As String
    Public Property HardwareID As String
    Public Property ImageURL As String
    Public Property Children As List(Of LHM)

End Class

Public Class LHMService

    Private ReadOnly client As New HttpClient()
    Private ReadOnly options As New JsonSerializerOptions With {.PropertyNameCaseInsensitive = True}

    Public Property Current As LHM

    Public Sub Refresh()
        Dim json As String = client.GetStringAsync("http://127.0.0.1:8085/data.json").Result
        Current = JsonSerializer.Deserialize(Of LHM)(json, options)
    End Sub

End Class