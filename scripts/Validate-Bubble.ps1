$ErrorActionPreference = 'Stop'
try {
  Add-Type -AssemblyName PresentationFramework
  Add-Type -AssemblyName PresentationCore
  Add-Type -AssemblyName WindowsBase
  $app = New-Object System.Windows.Application
  $xamlPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'source\SpeechBubble.Lite.xaml'
  $xaml = [System.IO.File]::ReadAllText($xamlPath, [System.Text.Encoding]::UTF8)
  $window = [System.Windows.Markup.XamlReader]::Parse($xaml)
  foreach ($name in @('BubbleRoot','BubbleOutline','TaskText','DismissButton')) {
    $element = $window.FindName($name)
    if ($null -eq $element) { throw "XAML name cannot be resolved: $name" }
    Write-Host "Found $name : $($element.GetType().FullName)"
  }
  $root = $window.FindName('BubbleRoot')
  if ($null -eq $root.RenderTransform -or $root.RenderTransform.GetType().Name -ne 'ScaleTransform') {
    throw 'BubbleRoot must have a ScaleTransform'
  }
  Write-Host "WPF bubble XAML validation PASSED"
  $window.Close()
}
catch {
  Write-Host "WPF bubble XAML validation FAILED"
  Write-Host $_.Exception.ToString()
  if ($_.Exception.InnerException) { Write-Host $_.Exception.InnerException.ToString() }
  exit 1
}
