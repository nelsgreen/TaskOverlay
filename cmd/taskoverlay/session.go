//go:build windows

package main

import (
	"encoding/json"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"time"
	"unsafe"
)

type SessionInfo struct {
	PID       int       `json:"pid"`
	Version   string    `json:"version"`
	StartedAt time.Time `json:"started_at"`
	Clean     bool      `json:"clean"`
}

func sessionPath() string { return filepath.Join(appDataDir(), "session.json") }

func markSessionStart() {
	_ = os.MkdirAll(appDataDir(), 0755)
	sessionStartedAt = time.Now()
	info := SessionInfo{PID: os.Getpid(), Version: AppVersion, StartedAt: sessionStartedAt, Clean: false}
	data, _ := json.MarshalIndent(info, "", "  ")
	_ = os.WriteFile(sessionPath(), data, 0644)
}

func markSessionEnd(clean bool) {
	started := sessionStartedAt
	if started.IsZero() {
		started = time.Now()
	}
	info := SessionInfo{PID: os.Getpid(), Version: AppVersion, StartedAt: started, Clean: clean}
	data, _ := json.MarshalIndent(info, "", "  ")
	_ = os.WriteFile(sessionPath(), data, 0644)
}

func readSessionInfo() (SessionInfo, bool) {
	data, err := os.ReadFile(sessionPath())
	if err != nil {
		return SessionInfo{}, false
	}
	var info SessionInfo
	if json.Unmarshal(data, &info) != nil {
		return SessionInfo{}, false
	}
	return info, true
}

func checkPreviousSession() (bool, bool) {
	info, ok := readSessionInfo()
	if !ok || info.Clean {
		return true, true
	}
	if isProcessRunning(info.PID) {
		logf("previous TaskOverlay process is still running pid=%d version=%s started=%s", info.PID, info.Version, info.StartedAt.Format(time.RFC3339))
		messageBox("TaskOverlay уже запущен. Закройте старое окно перед запуском новой версии.", "TaskOverlay")
		return true, false
	}
	logf("previous session was not clean pid=%d version=%s started=%s", info.PID, info.Version, info.StartedAt.Format(time.RFC3339))
	return false, true
}

func acquireSingleInstance() bool {
	name := utf16Ptr("Global\\TaskOverlay.SingleInstance")
	h, _, _ := procCreateMutexW.Call(0, 1, uintptr(unsafe.Pointer(name)))
	if h == 0 {
		logf("CreateMutexW failed, continuing without mutex")
		return true
	}
	instanceMutex = h
	lastErr, _, _ := procGetLastError.Call()
	if lastErr == ERROR_ALREADY_EXISTS {
		logf("another TaskOverlay instance detected by mutex")
		messageBox("TaskOverlay уже запущен. Закройте старое окно перед запуском новой версии.", "TaskOverlay")
		return false
	}
	logf("single instance lock acquired")
	return true
}

func isProcessRunning(pid int) bool {
	if pid <= 0 || pid == os.Getpid() {
		return false
	}
	out, err := exec.Command("tasklist", "/FI", fmt.Sprintf("PID eq %d", pid), "/FO", "CSV", "/NH").CombinedOutput()
	if err != nil {
		logf("tasklist check failed pid=%d err=%v", pid, err)
		return false
	}
	return strings.Contains(string(out), fmt.Sprintf("\"%d\"", pid)) || strings.Contains(string(out), fmt.Sprintf(",%d,", pid))
}

func messageBox(text, title string) {
	procMessageBoxW.Call(0, uintptr(unsafe.Pointer(utf16Ptr(text))), uintptr(unsafe.Pointer(utf16Ptr(title))), MB_OK|MB_ICONWARNING)
}
