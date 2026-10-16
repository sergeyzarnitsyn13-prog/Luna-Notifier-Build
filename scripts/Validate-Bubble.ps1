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
  Write-Host "WPF markup parse PASSED"
  $exe = Join-Path (Split-Path $PSScriptRoot -Parent) 'bin\Release\Luna.OwnerTest.exe'
  $assembly = [System.Reflection.Assembly]::LoadFrom($exe)
  $resourceNames = $assembly.GetManifestResourceNames()
  foreach ($name in @('SpeechBubbleXaml','MoonPng')) {
    if ($resourceNames -notcontains $name) { throw "Missing embedded resource: $name" }
    Write-Host "Embedded resource found: $name"
  }
  $flags = [System.Reflection.BindingFlags]'NonPublic,Public,Instance'
  $moonType = $assembly.GetType('LunaMoon', $true)
  $alertType = $assembly.GetType('AlertItem', $true)
  $bubbleType = $assembly.GetType('LunaBubble', $true)
  $moon = $moonType.GetConstructor($flags, $null, [Type[]]@(), $null).Invoke(@())
  $alert = $alertType.GetConstructor($flags, $null, [Type[]]@(), $null).Invoke(@())
  $alertType.GetField('Title', $flags).SetValue($alert, '')
  $alertType.GetField('Body', $flags).SetValue($alert, 'Test bubble constructor on Windows')
  $dismiss = [System.Action]{ }
  $ctor = $bubbleType.GetConstructors($flags) | Select-Object -First 1
  $bubble = $ctor.Invoke(@($moon,$alert,$dismiss))
  if ($null -eq $bubble) { throw 'Bubble constructor returned null' }
  Write-Host "Compiled bubble constructor PASSED"
  $show = $bubbleType.GetMethod('ShowAnimated', $flags)
  $show.Invoke($bubble, @())
  $field = $bubbleType.GetField('window', $flags)
  $visibleWindow = $field.GetValue($bubble)
  if (-not $visibleWindow.IsVisible) { throw 'Bubble window was not marked visible by ShowAnimated' }
  Write-Host "Bubble window shown: visible=$($visibleWindow.IsVisible) width=$($visibleWindow.ActualWidth) height=$($visibleWindow.ActualHeight) at ($($visibleWindow.Left),$($visibleWindow.Top))"
  if ($visibleWindow.ActualWidth -lt 150 -or $visibleWindow.ActualHeight -lt 60) { throw 'Bubble window has unexpectedly small dimensions' }
  $visibleWindow.Close()
  $app.Shutdown()
  Write-Host "Compiled bubble ShowAnimated PASSED"
  Write-Host "WPF bubble XAML validation PASSED"
  $window.Close()
}
catch {
  Write-Host "WPF bubble XAML validation FAILED"
  Write-Host $_.Exception.ToString()
  if ($_.Exception.InnerException) { Write-Host $_.Exception.InnerException.ToString() }
  exit 1
}
