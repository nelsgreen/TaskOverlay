//go:build windows

package main

import (
	"encoding/json"
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"runtime/debug"
	"strings"
	"time"
)

func (a *App) exportDiagnostics() {
	defer func() {
		if r := recover(); r != nil {
			logf("panic in exportDiagnostics: %v", r)
			logf("stack:\n%s", string(debug.Stack()))
			a.setStatus("Диагностика не создана. Ошибка записана в лог")
		}
	}()
	logf("diagnostics begin")
	path := diagnosticsPath()
	logf("diagnostics path=%s", path)
	var b strings.Builder
	b.WriteString("TaskOverlay diagnostics\r\n")
	b.WriteString("version: " + AppVersion + "\r\n")
	b.WriteString("time: " + time.Now().Format("2006-01-02 15:04:05") + "\r\n")
	b.WriteString("go: " + runtime.Version() + "\r\n")
	b.WriteString("os: " + runtime.GOOS + " " + runtime.GOARCH + "\r\n")
	if exe, err := os.Executable(); err == nil {
		b.WriteString("exe: " + exe + "\r\n")
	} else {
		b.WriteString("exe_error: " + err.Error() + "\r\n")
	}
	b.WriteString("app_data: " + appDataDir() + "\r\n")
	b.WriteString("state_path: " + statePath() + "\r\n")
	b.WriteString("session_path: " + sessionPath() + "\r\n")
	if info, ok := readSessionInfo(); ok {
		b.WriteString(fmt.Sprintf("session: pid=%d version=%s clean=%v started=%s\r\n", info.PID, info.Version, info.Clean, info.StartedAt.Format(time.RFC3339)))
	} else {
		b.WriteString("session: not available\r\n")
	}
	b.WriteString("log_path: " + logFilePath + "\r\n")
	activeBounds := a.activeBounds()
	passiveBounds := a.passiveBounds()
	b.WriteString(fmt.Sprintf(
		"overlay: active=%v transparency_mode=solid_background global_window_alpha=false background_intensity=%d text_opacity=%d auto_hide_delay_ms=%d active_bounds=%d,%d,%d,%d passive_bounds=%d,%d,%d,%d\r\n",
		a.isActiveMode(),
		a.state.Settings.BgAlpha,
		a.state.Settings.TextAlpha,
		a.state.Settings.AutoHideDelayMS,
		activeBounds.Left,
		activeBounds.Top,
		activeBounds.Right,
		activeBounds.Bottom,
		passiveBounds.Left,
		passiveBounds.Top,
		passiveBounds.Right,
		passiveBounds.Bottom,
	))
	logf("diagnostics state read begin")
	b.WriteString("\r\nSTATE\r\n")
	if data, err := json.MarshalIndent(a.state, "", "  "); err == nil {
		b.Write(data)
	} else {
		b.WriteString("state marshal error: " + err.Error())
	}
	logf("diagnostics log tail begin")
	b.WriteString("\r\n\r\nRECENT LOG\r\n")
	b.WriteString(readLastLogLinesSafe(logFilePath, 128*1024))
	logf("diagnostics write begin")
	if err := os.MkdirAll(filepath.Dir(path), 0755); err != nil {
		logf("diagnostics mkdir failed path=%s err=%v", filepath.Dir(path), err)
		a.setStatus("Не удалось создать папку диагностики")
		return
	}
	if err := saveBytesAtomic(path, []byte(b.String())); err != nil {
		logf("diagnostics write failed path=%s err=%v", path, err)
		a.setStatus("Не удалось сохранить диагностику")
		return
	}
	logf("diagnostics exported path=%s", path)
	a.setStatus("Диагностика создана на рабочем столе")
}

func diagnosticsPath() string {
	desktop := filepath.Join(os.Getenv("USERPROFILE"), "Desktop")
	if _, err := os.Stat(desktop); err != nil {
		desktop = os.Getenv("USERPROFILE")
	}
	if desktop == "" {
		desktop = appDataDir()
	}
	name := "TaskOverlay_diagnostics_" + time.Now().Format("20060102_150405") + ".txt"
	return filepath.Join(desktop, name)
}

func readLastLogLines(path string, limit int) string {
	return readLastLogLinesSafe(path, int64(limit)*1024)
}

func readLastLogLinesSafe(path string, maxBytes int64) (out string) {
	defer func() {
		if r := recover(); r != nil {
			out = fmt.Sprintf("log tail panic: %v\r\n", r)
			logf("panic in readLastLogLinesSafe: %v", r)
		}
	}()
	if path == "" {
		return "log path is empty\r\n"
	}
	if maxBytes < 4096 {
		maxBytes = 4096
	}
	f, err := os.Open(path)
	if err != nil {
		return "log read error: " + err.Error() + "\r\n"
	}
	defer f.Close()
	st, err := f.Stat()
	if err != nil {
		return "log stat error: " + err.Error() + "\r\n"
	}
	start := int64(0)
	if st.Size() > maxBytes {
		start = st.Size() - maxBytes
	}
	buf := make([]byte, st.Size()-start)
	n, err := f.ReadAt(buf, start)
	if err != nil && err != io.EOF {
		return "log read tail error: " + err.Error() + "\r\n"
	}
	buf = buf[:n]
	text := strings.ReplaceAll(string(buf), "\r\n", "\n")
	text = strings.ReplaceAll(text, "\r", "\n")
	lines := strings.Split(text, "\n")
	const maxLines = 220
	if len(lines) > maxLines {
		lines = lines[len(lines)-maxLines:]
	}
	return strings.Join(lines, "\r\n")
}

func (a *App) openLogFolder() {
	logf("open logs folder")
	_ = os.MkdirAll(logsDir(), 0755)
	if err := exec.Command("explorer.exe", logsDir()).Start(); err != nil {
		logf("open logs failed err=%v", err)
		a.setStatus("Не удалось открыть папку логов")
	}
}

func (a *App) resetWindow() {
	logf("reset window")
	a.state.Settings.X = 1200
	a.state.Settings.Y = 120
	a.state.Settings.W = 620
	a.state.Settings.H = 520
	a.setOverlayMode(a.isActiveMode(), "reset_window")
	saveState(a.state)
	a.setStatus("Положение окна сброшено")
}
