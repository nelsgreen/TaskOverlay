//go:build windows

package main

import (
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
)

func createShortcutSafe() {
	exePath, err := os.Executable()
	if err != nil {
		logf("shortcut skipped, executable error=%v", err)
		return
	}
	desktop := filepath.Join(os.Getenv("USERPROFILE"), "Desktop")
	if _, err := os.Stat(desktop); err != nil {
		logf("shortcut skipped, desktop not found path=%s err=%v", desktop, err)
		return
	}
	lnk := filepath.Join(desktop, "TaskOverlay.lnk")
	dir := filepath.Dir(exePath)
	icon := filepath.Join(dir, "TaskOverlay.ico")
	if _, err := os.Stat(icon); err != nil {
		icon = exePath
	}
	ps := fmt.Sprintf("$s=(New-Object -ComObject WScript.Shell).CreateShortcut('%s');$s.TargetPath='%s';$s.WorkingDirectory='%s';$s.IconLocation='%s';$s.Save()", psq(lnk), psq(exePath), psq(dir), psq(icon))
	if err := exec.Command("powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", ps).Start(); err != nil {
		logf("shortcut update failed path=%s err=%v", lnk, err)
	} else {
		logf("shortcut update requested path=%s target=%s", lnk, exePath)
	}
}

func psq(s string) string { return strings.ReplaceAll(s, "'", "''") }
