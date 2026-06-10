//go:build windows

package main

import (
	"fmt"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"time"
	"unsafe"
)

func (a *App) checkDue() bool {
	now := time.Now().Format("15:04")
	changed := false
	for i := range a.state.Tasks {
		t := &a.state.Tasks[i]
		if t.Done || t.DueHHMM == "" || t.Ack {
			continue
		}
		if now >= t.DueHHMM {
			if !t.Blink {
				changed = true
			}
			t.Blink = true
		}
	}
	if changed {
		a.scheduleSave("due_changed")
	}
	return changed
}

func (a *App) hasBlinkingTasks() bool {
	for _, task := range a.state.Tasks {
		if !task.Done && task.Blink {
			return true
		}
	}
	return false
}

func (a *App) export(days int) {
	desktop := filepath.Join(os.Getenv("USERPROFILE"), "Desktop")
	if _, err := os.Stat(desktop); err != nil {
		desktop = os.Getenv("USERPROFILE")
	}
	name := fmt.Sprintf("TaskOverlay_export_%s.txt", time.Now().Format("20060102_1504"))
	path := filepath.Join(desktop, name)
	var b strings.Builder
	b.WriteString("TaskOverlay export\r\n")
	b.WriteString(time.Now().Format("2006-01-02 15:04"))
	b.WriteString("\r\n\r\n")
	var since time.Time
	if days > 0 {
		since = time.Now().AddDate(0, 0, -days)
	}
	for _, t := range a.state.Tasks {
		if days > 0 && t.CreatedAt.Before(since) {
			continue
		}
		prefix := "[ ] "
		if t.Done {
			prefix = "[x] "
		}
		if t.ParentID != 0 {
			prefix = "  - " + prefix
		}
		b.WriteString(prefix)
		b.WriteString(t.Text)
		if t.Priority > 0 {
			b.WriteString(" | P" + strconv.Itoa(t.Priority))
		}
		if t.InWork {
			b.WriteString(" | в работе")
		}
		if t.DueHHMM != "" {
			b.WriteString(" | " + t.DueHHMM)
		}
		if strings.TrimSpace(t.Description) != "" {
			b.WriteString(" | есть описание")
		}
		b.WriteString(" | создано: " + t.CreatedAt.Format("2006-01-02 15:04"))
		if t.DoneAt != nil {
			b.WriteString(" | выполнено: " + t.DoneAt.Format("2006-01-02 15:04"))
		}
		b.WriteString("\r\n")
	}
	if err := os.WriteFile(path, []byte(b.String()), 0644); err != nil {
		a.setStatus("Не удалось выгрузить файл")
	} else {
		a.setStatus("Экспорт создан на рабочем столе: " + name)
	}
}

func (a *App) setStatus(s string) {
	a.status = s
	a.statusUntil = time.Now().Add(4 * time.Second)
	procSetTimer.Call(uintptr(a.hwnd), TIMER_STATUS, 1000, 0)
}

func (a *App) captureWindowRect() {
	if a == nil || a.hwnd == 0 || a.modeChanging || !a.isActiveMode() {
		return
	}
	var r RECT
	procGetWindowRect.Call(uintptr(a.hwnd), uintptr(unsafe.Pointer(&r)))
	if r.Right > r.Left && r.Bottom > r.Top {
		a.state.Settings.X = r.Left
		a.state.Settings.Y = r.Top
		a.state.Settings.W = r.Right - r.Left
		a.state.Settings.H = r.Bottom - r.Top
	}
}
