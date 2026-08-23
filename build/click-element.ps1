<#
.SYNOPSIS
    Clicks a UI Automation element in a running window, by its accessible name.

.DESCRIPTION
    A development aid for driving the application during manual verification. Works through UI
    Automation rather than synthetic mouse input, so it does not depend on window position and
    fails loudly when an element is missing instead of clicking whatever happens to be there.

    That it works at all is also a check worth having: an element this script cannot find by name
    is an element a screen reader cannot find either.

.PARAMETER ProcessName
    Process owning the window, without the .exe.

.PARAMETER Name
    The element's accessible name.

.PARAMETER Pattern
    Which automation pattern to use. Invoke suits buttons; Select suits radio buttons and list
    items; Toggle suits switches and checkboxes.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ProcessName,
    [Parameter(Mandatory = $true)][string]$Name,
    [ValidateSet('Invoke', 'Select', 'Toggle')][string]$Pattern = 'Invoke'
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$process = Get-Process -Name $ProcessName -ErrorAction Stop | Select-Object -First 1

if ($process.MainWindowHandle -eq 0) {
    throw "'$ProcessName' has no main window."
}

$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)

if (-not $root) {
    throw 'Could not attach to the window through UI Automation.'
}

$condition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, $Name)

$element = $root.FindFirst(
    [System.Windows.Automation.TreeScope]::Descendants, $condition)

if (-not $element) {
    throw "No element named '$Name' was found. Check the AutomationProperties.Name on it."
}

switch ($Pattern) {
    'Invoke' {
        $p = $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $p.Invoke()
    }
    'Select' {
        $p = $element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $p.Select()
    }
    'Toggle' {
        $p = $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        $p.Toggle()
    }
}

"Performed $Pattern on '$Name'"
