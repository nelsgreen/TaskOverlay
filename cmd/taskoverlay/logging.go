//go:build windows

package main

import (
	"fmt"
	"os"
	"path/filepath"
	"runtime/debug"
	"time"
)

func initLogger() {
	_ = os.MkdirAll(logsDir(), 0755)
	logFilePath = filepath.Join(logsDir(), "TaskOverlay_"+time.Now().Format("20060102")+".log")
	logf("logger initialized")
}

func initCrashOutput() {
	path := filepath.Join(logsDir(), "TaskOverlay_crash_"+time.Now().Format("20060102")+".log")
	f, err := os.OpenFile(path, os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0644)
	if err != nil {
		logf("crash output open failed path=%s err=%v", path, err)
		return
	}
	crashFile = f
	if err := debug.SetCrashOutput(f, debug.CrashOptions{}); err != nil {
		logf("crash output setup failed path=%s err=%v", path, err)
		return
	}
	logf("crash output enabled path=%s", path)
}

func debugInputf(format string, args ...interface{}) {
	if DebugInput {
		logf(format, args...)
	}
}

func logf(format string, args ...interface{}) {
	if logFilePath == "" {
		base := os.Getenv("APPDATA")
		if base == "" {
			base = os.TempDir()
		}
		logFilePath = filepath.Join(base, "TaskOverlay", "logs", "TaskOverlay_"+time.Now().Format("20060102")+".log")
	}
	line := time.Now().Format("2006-01-02 15:04:05.000") + " " + fmt.Sprintf(format, args...) + "\r\n"
	logMu.Lock()
	defer logMu.Unlock()
	_ = os.MkdirAll(filepath.Dir(logFilePath), 0755)
	f, err := os.OpenFile(logFilePath, os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0644)
	if err != nil {
		return
	}
	_, _ = f.WriteString(line)
	_ = f.Close()
}
