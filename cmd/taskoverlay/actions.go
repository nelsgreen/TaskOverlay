//go:build windows

package main

import (
	"fmt"
	"runtime/debug"
	"strconv"
	"strings"
	"syscall"
	"time"
	"unsafe"
)

func (a *App) handleClick(x, y int32) {
	a.windowActive = true
	a.overlayActive = true
	applyAlpha()
	a.finishEdit(true)
	for i := len(a.actions) - 1; i >= 0; i-- {
		act := a.actions[i]
		if inRect(x, y, act.Rect) {
			a.perform(act)
			return
		}
	}
	if y < a.scale(36) {
		procReleaseCapture.Call()
		procSendMessageW.Call(uintptr(a.hwnd), WM_NCLBUTTONDOWN, HTCAPTION, 0)
		a.captureWindowRect()
		saveState(a.state)
	}
}

func (a *App) perform(act Action) {
	defer func() {
		if r := recover(); r != nil {
			logf("panic in action kind=%s task=%d value=%d: %v", act.Kind, act.TaskID, act.Value, r)
			logf("stack:\n%s", string(debug.Stack()))
			a.setStatus("Ошибка действия записана в лог")
			invalidate()
		}
	}()
	logf("action kind=%s task=%d value=%d", act.Kind, act.TaskID, act.Value)
	if a.performDynamicAction(act.Kind) {
		a.scheduleSave(act.Kind)
		invalidate()
		logf("action invalidate done kind=%s", act.Kind)
		return
	}
	switch act.Kind {
	case "close":
		procSendMessageW.Call(uintptr(a.hwnd), WM_CLOSE, 0, 0)
	case "settings":
		logf("settings toggle begin open=%v", a.settingsOpen)
		a.settingsOpen = !a.settingsOpen
		a.settingsPaintLogged = false
		logf("settings toggle end open=%v", a.settingsOpen)
		invalidate()
		return
	case "add":
		a.addTask(0)
	case "toggle_done":
		a.updateTask(act.TaskID, func(t *Task) {
			t.Done = !t.Done
			if t.Done {
				now := time.Now()
				t.DoneAt = &now
				t.Blink = false
				t.Ack = true
			} else {
				t.DoneAt = nil
				t.Ack = false
			}
		})
	case "expand":
		a.updateTask(act.TaskID, func(t *Task) { t.Expanded = !t.Expanded })
	case "priority":
		a.updateTask(act.TaskID, func(t *Task) { t.Priority = (t.Priority + 1) % 4 })
	case "add_child":
		a.addTask(act.TaskID)
		a.updateTask(act.TaskID, func(t *Task) { t.Expanded = true })
	case "details":
		if a.detailsTaskID == act.TaskID {
			a.detailsTaskID = 0
		} else {
			a.detailsTaskID = act.TaskID
		}
	case "inwork":
		for i := range a.state.Tasks {
			a.state.Tasks[i].InWork = false
		}
		a.updateTask(act.TaskID, func(t *Task) { t.InWork = true })
	case "delete":
		a.deleteTask(act.TaskID)
	case "edit_text":
		a.startEdit(act.TaskID, "text", act.Rect)
	case "edit_due":
		a.startEdit(act.TaskID, "due", act.Rect)
	case "edit_description":
		a.startEdit(act.TaskID, "description", act.Rect)
	case "drop_bg":
		if a.dropdown == "bg" {
			a.dropdown = ""
		} else {
			a.dropdown = "bg"
		}
	case "drop_text":
		if a.dropdown == "text" {
			a.dropdown = ""
		} else {
			a.dropdown = "text"
		}
	case "bg_alpha_minus":
		if a.state.Settings.BgAlpha > 35 {
			a.state.Settings.BgAlpha -= 15
			a.state.Settings.Alpha = a.state.Settings.BgAlpha
			applyAlpha()
		}
	case "bg_alpha_plus":
		if a.state.Settings.BgAlpha < 255 {
			a.state.Settings.BgAlpha = byte(minInt(255, int(a.state.Settings.BgAlpha)+15))
			a.state.Settings.Alpha = a.state.Settings.BgAlpha
			applyAlpha()
		}
	case "text_alpha_minus":
		if a.state.Settings.TextAlpha > 35 {
			a.state.Settings.TextAlpha -= 15
		}
	case "text_alpha_plus":
		if a.state.Settings.TextAlpha < 255 {
			a.state.Settings.TextAlpha = byte(minInt(255, int(a.state.Settings.TextAlpha)+15))
		}
	case "font_minus":
		if a.state.Settings.FontSize > 12 {
			a.state.Settings.FontSize -= 1
		}
	case "font_plus":
		if a.state.Settings.FontSize < 36 {
			a.state.Settings.FontSize += 1
		}
	case "toggle_bold":
		a.state.Settings.Bold = !a.state.Settings.Bold
	case "toggle_shadow":
		a.state.Settings.Shadow = !a.state.Settings.Shadow
	case "toggle_outline":
		a.state.Settings.Outline = !a.state.Settings.Outline
	case "toggle_border":
		a.state.Settings.ShowBorders = !a.state.Settings.ShowBorders
	case "toggle_completed":
		a.state.Settings.CompletedExpanded = !a.state.Settings.CompletedExpanded
	case "toggle_collapse_done":
		a.state.Settings.CompletedExpanded = !a.state.Settings.CompletedExpanded
	case "toggle_done_style":
		a.state.Settings.DoneStyle = (a.state.Settings.DoneStyle + 1) % 2
	case "marker_dot":
		a.state.Settings.PassiveMarkerStyle = "dot"
	case "marker_dash":
		a.state.Settings.PassiveMarkerStyle = "dash"
	case "marker_arrow":
		a.state.Settings.PassiveMarkerStyle = "arrow"
	case "marker_checkbox":
		a.state.Settings.PassiveMarkerStyle = "checkbox"
	case "toggle_show_completed_active":
		a.state.Settings.ShowCompletedActive = !a.state.Settings.ShowCompletedActive
	case "export_1":
		a.export(1)
	case "export_7":
		a.export(7)
	case "export_30":
		a.export(30)
	case "export_all":
		a.export(0)
	case "diag_export":
		logf("diag_export action begin")
		a.exportDiagnostics()
		logf("diag_export action end")
		invalidate()
		return
	case "open_logs":
		a.openLogFolder()
	case "reset_window":
		a.resetWindow()
	}
	a.scheduleSave(act.Kind)
	invalidate()
	logf("action invalidate done kind=%s", act.Kind)
}

func (a *App) performDynamicAction(kind string) bool {
	if strings.HasPrefix(kind, "set_bg_") {
		idx, err := strconv.Atoi(strings.TrimPrefix(kind, "set_bg_"))
		opts := bgOptions()
		if err == nil && idx >= 0 && idx < len(opts) {
			a.state.Settings.BgColor = opts[idx].Color
			a.dropdown = ""
			return true
		}
	}
	if strings.HasPrefix(kind, "set_text_") {
		idx, err := strconv.Atoi(strings.TrimPrefix(kind, "set_text_"))
		opts := textOptions()
		if err == nil && idx >= 0 && idx < len(opts) {
			a.state.Settings.TextColor = opts[idx].Color
			a.dropdown = ""
			return true
		}
	}
	return false
}

func (a *App) addTask(parent int64) {
	if a.state.NextID < 1 {
		a.state.NextID = 1
	}
	title := "Новая задача"
	if parent != 0 {
		title = "Новая подзадача"
	}
	description := ""
	if clip, err := clipboardText(); err == nil && clip != "" {
		ct, cd := deriveTitleAndDescription(clip)
		if ct != "" {
			title = ct
			description = cd
		}
		logf("clipboard prefill used=true chars=%d title_chars=%d description_chars=%d", len([]rune(clip)), len([]rune(title)), len([]rune(description)))
	} else if err != nil {
		logf("clipboard prefill used=false err=%v", err)
	}
	id := a.state.NextID
	a.state.Tasks = append(a.state.Tasks, Task{ID: id, ParentID: parent, Text: title, Description: description, CreatedAt: time.Now(), Expanded: true})
	a.state.NextID++
	a.detailsTaskID = id
	a.startEdit(id, "text", RECT{})
	a.editCreatedNew = true
}

func (a *App) updateTask(id int64, fn func(*Task)) {
	for i := range a.state.Tasks {
		if a.state.Tasks[i].ID == id {
			fn(&a.state.Tasks[i])
			return
		}
	}
}

func (a *App) deleteTask(id int64) {
	var out []Task
	for _, t := range a.state.Tasks {
		if t.ID == id || t.ParentID == id {
			continue
		}
		out = append(out, t)
	}
	a.state.Tasks = out
}

func (a *App) hasChildren(id int64) bool {
	for _, t := range a.state.Tasks {
		if t.ParentID == id {
			return true
		}
	}
	return false
}

func (a *App) startEdit(taskID int64, kind string, r RECT) {
	defer func() {
		if rec := recover(); rec != nil {
			logf("panic in startEdit task=%d kind=%s: %v", taskID, kind, rec)
			logf("stack:\n%s", string(debug.Stack()))
			a.editActive = false
			a.setStatus("Ошибка редактирования записана в лог")
			invalidate()
		}
	}()
	a.finishEdit(true)
	text := ""
	for _, t := range a.state.Tasks {
		if t.ID == taskID {
			switch kind {
			case "due":
				text = t.DueHHMM
			case "description":
				text = t.Description
			default:
				text = t.Text
			}
			break
		}
	}
	a.editActive = true
	a.editTaskID = taskID
	a.editKind = kind
	a.editText = text
	a.editOriginal = text
	a.editReplaceOnType = true
	a.editCreatedNew = false
	a.windowActive = true
	a.overlayActive = true
	procKillTimer.Call(uintptr(a.hwnd), TIMER_PASSIVE)
	applyAlpha()
	logf("edit begin task=%d field=%s original_len=%d replace_on_type=%v", taskID, kind, len([]rune(text)), a.editReplaceOnType)
	procSetFocus.Call(uintptr(a.hwnd))
	a.setStatus("Редактирование: Enter - сохранить, Esc - отменить")
	invalidate()
}

func (a *App) finishEdit(save bool) {
	defer func() {
		if rec := recover(); rec != nil {
			logf("panic in finishEdit task=%d kind=%s save=%v: %v", a.editTaskID, a.editKind, save, rec)
			logf("stack:\n%s", string(debug.Stack()))
			a.editActive = false
			a.editText = ""
			a.editOriginal = ""
			a.editReplaceOnType = false
			a.editCreatedNew = false
			a.setStatus("Ошибка сохранения редактирования записана в лог")
			invalidate()
		}
	}()
	if !a.editActive {
		return
	}
	taskID := a.editTaskID
	kind := a.editKind
	text := a.editText
	createdNew := a.editCreatedNew
	logf("edit finish begin task=%d kind=%s save=%v len=%d", taskID, kind, save, len([]rune(text)))
	a.editActive = false
	a.editTaskID = 0
	a.editKind = ""
	a.editText = ""
	a.editOriginal = ""
	a.editReplaceOnType = false
	a.editCreatedNew = false
	if !save {
		if createdNew {
			a.deleteTask(taskID)
			a.scheduleSave("edit_cancel_new")
		}
		applyAlpha()
		a.schedulePassiveMode()
		logf("edit cancel task=%d kind=%s removed_draft=%v", taskID, kind, createdNew)
		invalidate()
		return
	}
	trimmed := strings.TrimSpace(text)
	if createdNew && trimmed == "" {
		a.deleteTask(taskID)
		a.scheduleSave("edit_empty_new")
		a.schedulePassiveMode()
		invalidate()
		return
	}
	a.updateTask(taskID, func(t *Task) {
		switch kind {
		case "due":
			trimmed = strings.TrimSpace(text)
			if trimmed == "" || validHHMM(trimmed) {
				t.DueHHMM = trimmed
				t.Ack = false
				t.Blink = false
			} else {
				a.setStatus("Время нужно указать в формате HH:MM")
			}
		case "description":
			t.Description = strings.TrimSpace(text)
		default:
			trimmed = strings.TrimSpace(text)
			if trimmed != "" {
				t.Text = trimmed
			}
		}
	})
	a.scheduleSave("edit_commit_" + kind)
	applyAlpha()
	a.schedulePassiveMode()
	logf("edit finish end task=%d kind=%s save=%v", taskID, kind, save)
	invalidate()
}

func (a *App) handleKeyDown(wParam uintptr) bool {
	if !a.editActive {
		return false
	}
	defer func() {
		if rec := recover(); rec != nil {
			logf("panic in handleKeyDown key=%d task=%d kind=%s: %v", wParam, a.editTaskID, a.editKind, rec)
			logf("stack:\n%s", string(debug.Stack()))
			a.editActive = false
			a.setStatus("Ошибка ввода записана в лог")
			invalidate()
		}
	}()
	debugInputf("edit keydown key=%d task=%d kind=%s replace=%v", wParam, a.editTaskID, a.editKind, a.editReplaceOnType)
	if wParam == VK_V && ctrlDown() {
		clip, err := clipboardText()
		if err == nil && clip != "" {
			if a.editReplaceOnType {
				a.editText = ""
				a.editReplaceOnType = false
			}
			if a.editKind == "text" || a.editKind == "due" {
				title, _ := deriveTitleAndDescription(clip)
				clip = title
			}
			a.editText += clip
			if len([]rune(a.editText)) > 4000 {
				a.editText = string([]rune(a.editText)[:4000])
			}
			invalidate()
		} else if err != nil {
			logf("clipboard paste failed err=%v", err)
		}
		return true
	}
	switch wParam {
	case VK_RETURN:
		a.finishEdit(true)
		return true
	case VK_ESCAPE:
		a.finishEdit(false)
		return true
	case VK_BACK:
		if a.editReplaceOnType {
			a.editText = ""
			a.editReplaceOnType = false
		} else {
			runes := []rune(a.editText)
			if len(runes) > 0 {
				a.editText = string(runes[:len(runes)-1])
			}
		}
		invalidate()
		return true
	case VK_DELETE:
		if a.editReplaceOnType {
			a.editText = ""
			a.editReplaceOnType = false
		}
		invalidate()
		return true
	}
	return true
}

func (a *App) handleChar(wParam uintptr) bool {
	if !a.editActive {
		return false
	}
	defer func() {
		if rec := recover(); rec != nil {
			logf("panic in handleChar char=%d task=%d kind=%s: %v", wParam, a.editTaskID, a.editKind, rec)
			logf("stack:\n%s", string(debug.Stack()))
			a.editActive = false
			a.setStatus("Ошибка ввода записана в лог")
			invalidate()
		}
	}()
	ch := rune(wParam)
	if ch < 32 {
		return true
	}
	debugInputf("edit char code=%d task=%d kind=%s len=%d replace=%v", wParam, a.editTaskID, a.editKind, len([]rune(a.editText)), a.editReplaceOnType)
	if a.editReplaceOnType {
		a.editText = ""
		a.editReplaceOnType = false
	}
	if a.editKind == "due" {
		if len([]rune(a.editText)) >= 5 {
			return true
		}
		if (ch < '0' || ch > '9') && ch != ':' {
			return true
		}
	} else {
		limit := 240
		if a.editKind == "description" {
			limit = 4000
		}
		if len([]rune(a.editText)) >= limit {
			return true
		}
	}
	a.editText += string(ch)
	invalidate()
	return true
}

func (a *App) scheduleSave(reason string) {
	if a == nil || a.hwnd == 0 {
		saveState(a.state)
		return
	}
	a.lastSaveReason = reason
	if !a.saveScheduled {
		a.saveScheduled = true
	}
	procSetTimer.Call(uintptr(a.hwnd), TIMER_SAVE, 350, 0)
}

func ctrlDown() bool {
	r, _, _ := procGetKeyState.Call(VK_CONTROL)
	return int16(r&0xffff) < 0
}

func clipboardText() (string, error) {
	avail, _, _ := procIsClipboardFormatAvailable.Call(CF_UNICODETEXT)
	if avail == 0 {
		return "", fmt.Errorf("unicode text not available")
	}
	r, _, err := procOpenClipboard.Call(0)
	if r == 0 {
		return "", err
	}
	defer procCloseClipboard.Call()
	h, _, err := procGetClipboardData.Call(CF_UNICODETEXT)
	if h == 0 {
		return "", err
	}
	ptr, _, err := procGlobalLock.Call(h)
	if ptr == 0 {
		return "", err
	}
	defer procGlobalUnlock.Call(h)
	var buf []uint16
	for i := uintptr(0); i < 8000*2; i += 2 {
		v := *(*uint16)(unsafe.Pointer(ptr + i))
		if v == 0 {
			break
		}
		buf = append(buf, v)
	}
	out := syscall.UTF16ToString(buf)
	out = strings.ReplaceAll(out, "\r\n", "\n")
	out = strings.TrimSpace(out)
	return out, nil
}

func deriveTitleAndDescription(clip string) (string, string) {
	clip = strings.TrimSpace(strings.ReplaceAll(clip, "\r\n", "\n"))
	if clip == "" {
		return "", ""
	}
	line := strings.TrimSpace(strings.SplitN(clip, "\n", 2)[0])
	r := []rune(line)
	if len(r) > 80 {
		line = string(r[:80]) + "..."
	}
	return line, clip
}
