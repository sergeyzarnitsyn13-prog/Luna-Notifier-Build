$ErrorActionPreference = 'Stop'
try {
  Add-Type -AssemblyName PresentationFramework
  Add-Type -AssemblyName PresentationCore
  Add-Type -AssemblyName WindowsBase
  $app = New-Object System.Windows.Application
  $xamlPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'source\SpeechBubble.Lite.xaml'
  $xaml = [System.IO.File]::ReadAllText($xamlPath, [System.Text.Encoding]::UTF8)
  $window = [System.Windows.Markup.XamlReader]::Parse($xaml)
  foreach ($name in @('BubbleRoot','BubbleOutline','AmbientGlow','MoonHalo','TitleText','TaskText','DismissButton','MoonScene','SeedCore','SeedSparkA','SeedSparkB','RevealFront')) {
    $element = $window.FindName($name)
    if ($null -eq $element) { throw "XAML name cannot be resolved: $name" }
    Write-Host "Found $name : $($element.GetType().FullName)"
  }
  $root = $window.FindName('BubbleRoot')
  if ($null -eq $root.RenderTransform -or $root.RenderTransform.GetType().Name -ne 'ScaleTransform') {
    throw 'BubbleRoot must have a ScaleTransform'
  }
  $ambient=$window.FindName('AmbientGlow')
  if ($ambient.GetType().FullName -ne 'System.Windows.Shapes.Path') { throw 'Ambient glow must be a thin stroked contour, not an ellipse fog' }
  if ($ambient.Fill.ToString() -ne '#00FFFFFF') { Write-Host "Ambient fill=$($ambient.Fill) (outline-only glow)" }
  Write-Host "WPF markup parse PASSED"
  $exe = Join-Path (Split-Path $PSScriptRoot -Parent) 'bin\Release\Luna.Notify.exe'
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
  # Sample the actual WPF visual tree on the Windows runner.
  # DispatcherFrame allows WPF animation clocks to progress while waiting.
  $framesDir = Join-Path (Split-Path $PSScriptRoot -Parent) 'animation-frames'
  New-Item -ItemType Directory -Force -Path $framesDir | Out-Null
  $samples = @(55,120,185,265,355,450,575,750)
  $elapsedBefore = 0
  foreach ($elapsed in $samples) {
    $wait = [Math]::Max(1, $elapsed-$elapsedBefore)
    $frame = New-Object System.Windows.Threading.DispatcherFrame
    $waiter = New-Object System.Windows.Threading.DispatcherTimer
    $waiter.Interval = [TimeSpan]::FromMilliseconds($wait)
    $handler = {
      $waiter.Stop()
      $frame.Continue = $false
    }.GetNewClosure()
    $waiter.Add_Tick($handler)
    $waiter.Start()
    [System.Windows.Threading.Dispatcher]::PushFrame($frame)
    $elapsedBefore=$elapsed

    $w = [Math]::Max(1,[int][Math]::Ceiling($visibleWindow.ActualWidth))
    $h = [Math]::Max(1,[int][Math]::Ceiling($visibleWindow.ActualHeight))
    $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($w,$h,96,96,[System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($visibleWindow)
    $encoder=New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $destination=Join-Path $framesDir ("MoonGlow-"+$elapsed.ToString("0000")+"ms.png")
    $output=[System.IO.File]::Create($destination)
    try {$encoder.Save($output)} finally {$output.Dispose()}
    Write-Host "Animation rendered: $destination ($w x $h)"
  }
  $visibleWindow.Close()
  $app.Shutdown()
  if ($null -eq $visibleWindow.FindName('MoonHalo') -or $null -eq $visibleWindow.FindName('AmbientGlow')) { throw 'Missing materialization elements' }
  if ($null -ne $visibleWindow.FindName('SeedCore') -and $null -ne $visibleWindow.FindName('RevealFront')) { Write-Host "MoonGlow gathering animation elements PASSED" } else { throw 'Missing MoonGlow gathering elements' }
  Write-Host "MoonGlow animation elements PASSED"
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
