param([switch]$Refresh)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
Add-Type -AssemblyName UIAutomationClient
$window = $null
# Prefer the original mascot's title; retain compatibility during upgrades.
$widgetNames = @(
    (-join ([char[]](0x7801, 0x529B, 0x82BD, 0x82BD))),
    (-join ([char[]](0x7801, 0x529B, 0x53EE, 0x5F53)))
)
foreach ($widgetName in $widgetNames) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $widgetName)
    $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Children, $condition)
    if ($null -ne $window) { break }
}
if ($null -eq $window) { throw 'Widget window not found in the current desktop session.' }
Write-Output "PID: $($window.Current.ProcessId)"
if ($Refresh) {
    $refreshCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'RefreshButton')
    $button = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $refreshCondition)
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 5
}
$nodes = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)
foreach ($node in $nodes) {
    if ($node.Current.Name) { Write-Output $node.Current.Name }
}
