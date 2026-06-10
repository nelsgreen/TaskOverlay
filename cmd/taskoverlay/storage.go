//go:build windows

package main

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"syscall"
	"time"
	"unsafe"
)

func loadState() State {
	st := defaultState()
	path := statePath()
	if data, err := os.ReadFile(path); err == nil {
		if err := json.Unmarshal(data, &st); err != nil {
			logf("state read failed path=%s err=%v", path, err)
			backupBytes("state_corrupt", data)
			st = defaultState()
		} else {
			logf("state loaded path=%s tasks=%d schema=%d app_version=%s", path, len(st.Tasks), st.SchemaVersion, st.AppVersion)
		}
	} else if os.IsNotExist(err) {
		if migrated, ok := loadMigratedState(); ok {
			st = migrated
			logf("state migrated tasks=%d", len(st.Tasks))
		} else {
			logf("state not found, using defaults")
		}
	} else {
		logf("state read error path=%s err=%v", path, err)
	}

	if st.SchemaVersion > SchemaVersion {
		logf("state schema is newer than app supports schema=%d supported=%d", st.SchemaVersion, SchemaVersion)
		backupCurrentState("newer_schema_" + strconv.Itoa(st.SchemaVersion))
	}
	oldVersion := st.AppVersion
	oldSchema := st.SchemaVersion
	normalizeState(&st, oldSchema)
	if oldVersion != "" && oldVersion != AppVersion {
		backupCurrentState("before_upgrade_" + safeName(oldVersion) + "_to_" + safeName(AppVersion))
		logf("upgrade detected from=%s to=%s", oldVersion, AppVersion)
	}
	st.SchemaVersion = SchemaVersion
	st.AppVersion = AppVersion
	saveState(st)
	return st
}

func loadMigratedState() (State, bool) {
	candidates := []string{
		filepath.Join(appDataDir(), "state_v3.json"),
	}
	for _, path := range candidates {
		data, err := os.ReadFile(path)
		if err != nil {
			continue
		}
		var st State
		if err := json.Unmarshal(data, &st); err != nil {
			logf("migration skipped, unreadable path=%s err=%v", path, err)
			backupBytes("migration_corrupt", data)
			continue
		}
		backupBytes("migrated_from_"+filepath.Base(path), data)
		return st, true
	}
	return State{}, false
}

func normalizeState(st *State, oldSchema int) {
	if st.Settings.W < 300 {
		st.Settings.W = 620
	}
	if st.Settings.H < 250 {
		st.Settings.H = 520
	}
	if st.Settings.W > 1400 {
		st.Settings.W = 620
	}
	if st.Settings.H > 1000 {
		st.Settings.H = 520
	}
	if st.Settings.X < -2000 || st.Settings.X > 6000 {
		st.Settings.X = 1200
	}
	if st.Settings.Y < -2000 || st.Settings.Y > 4000 {
		st.Settings.Y = 120
	}
	if st.Settings.FontSize < 10 {
		st.Settings.FontSize = 20
	}
	if st.Settings.FontSize > 44 {
		st.Settings.FontSize = 20
	}
	if st.Settings.Alpha < 40 {
		st.Settings.Alpha = 230
	}
	if st.Settings.BgAlpha == 0 {
		st.Settings.BgAlpha = st.Settings.Alpha
	}
	if st.Settings.BgAlpha < 35 {
		st.Settings.BgAlpha = 170
	}
	if st.Settings.TextAlpha == 0 {
		st.Settings.TextAlpha = 255
	}
	if oldSchema < 13 {
		st.Settings.CompletedExpanded = true
	}
	if oldSchema < 14 {
		st.Settings.ShowCompletedActive = true
	}
	switch st.Settings.PassiveMarkerStyle {
	case "dot", "dash", "arrow", "checkbox":
	default:
		st.Settings.PassiveMarkerStyle = "dot"
	}
	if st.NextID < 1 {
		st.NextID = 1
	}
	for i := range st.Tasks {
		if st.Tasks[i].ID >= st.NextID {
			st.NextID = st.Tasks[i].ID + 1
		}
		if st.Tasks[i].CreatedAt.IsZero() {
			st.Tasks[i].CreatedAt = time.Now()
		}
	}
}

func defaultState() State {
	now := time.Now()
	return State{
		SchemaVersion: SchemaVersion,
		AppVersion:    AppVersion,
		NextID:        3,
		Settings:      Settings{X: 1200, Y: 120, W: 620, H: 520, BgColor: rgb(22, 25, 30), TextColor: rgb(255, 232, 120), BorderColor: rgb(150, 150, 150), Alpha: 170, BgAlpha: 170, TextAlpha: 255, FontSize: 20, Bold: false, Shadow: true, Outline: false, ShowBorders: false, DoneStyle: 0, CollapseDone: false, CompletedExpanded: true, PassiveMarkerStyle: "dot", ShowCompletedActive: true},
		Tasks: []Task{
			{ID: 1, Text: "Редактируйте текст задачи кликом", CreatedAt: now, Expanded: true},
			{ID: 2, Text: "Кнопка + добавляет задачу", CreatedAt: now, Expanded: true},
		},
	}
}

func saveState(st State) {
	path := statePath()
	if err := os.MkdirAll(filepath.Dir(path), 0755); err != nil {
		logf("save mkdir failed path=%s err=%v", path, err)
		return
	}
	st.SchemaVersion = SchemaVersion
	st.AppVersion = AppVersion
	data, err := json.MarshalIndent(st, "", "  ")
	if err != nil {
		logf("save marshal failed err=%v", err)
		return
	}
	if err := saveBytesAtomic(path, data); err != nil {
		logf("save atomic failed path=%s err=%v", path, err)
		return
	}
}

func saveBytesAtomic(path string, data []byte) error {
	dir := filepath.Dir(path)
	if err := os.MkdirAll(dir, 0755); err != nil {
		return err
	}
	tmp, err := os.CreateTemp(dir, filepath.Base(path)+"-*.tmp")
	if err != nil {
		return err
	}
	tmpName := tmp.Name()
	defer os.Remove(tmpName)
	if _, err := tmp.Write(data); err != nil {
		_ = tmp.Close()
		return err
	}
	_ = tmp.Sync()
	if err := tmp.Close(); err != nil {
		return err
	}
	if _, err := os.Stat(path); os.IsNotExist(err) {
		return os.Rename(tmpName, path)
	}
	bak := path + ".bak"
	_ = os.Remove(bak)
	if err := replaceFileWindows(path, tmpName, bak); err != nil {
		// Fallback for environments where ReplaceFileW is blocked.
		if err2 := os.Rename(tmpName, path); err2 != nil {
			return fmt.Errorf("replacefile=%v rename=%v", err, err2)
		}
	}
	return nil
}

func replaceFileWindows(dst, src, bak string) error {
	dst16, err := syscall.UTF16PtrFromString(dst)
	if err != nil {
		return err
	}
	src16, err := syscall.UTF16PtrFromString(src)
	if err != nil {
		return err
	}
	var bakPtr *uint16
	if bak != "" {
		bakPtr, _ = syscall.UTF16PtrFromString(bak)
	}
	r, _, e := procReplaceFileW.Call(
		uintptr(unsafe.Pointer(dst16)),
		uintptr(unsafe.Pointer(src16)),
		uintptr(unsafe.Pointer(bakPtr)),
		0, 0, 0,
	)
	if r == 0 {
		if e != syscall.Errno(0) {
			return e
		}
		return fmt.Errorf("ReplaceFileW failed")
	}
	return nil
}

func statePath() string {
	return filepath.Join(appDataDir(), "state.json")
}

func appDataDir() string {
	base := os.Getenv("APPDATA")
	if base == "" {
		base = os.TempDir()
	}
	return filepath.Join(base, "TaskOverlay")
}

func logsDir() string {
	return filepath.Join(appDataDir(), "logs")
}

func backupDir() string {
	return filepath.Join(appDataDir(), "backup")
}

func backupBytes(prefix string, data []byte) {
	_ = os.MkdirAll(backupDir(), 0755)
	path := filepath.Join(backupDir(), safeName(prefix)+"_"+time.Now().Format("20060102_150405")+".json")
	if err := os.WriteFile(path, data, 0644); err != nil {
		logf("backup write failed path=%s err=%v", path, err)
	} else {
		logf("backup created path=%s", path)
	}
}

func backupCurrentState(prefix string) {
	data, err := os.ReadFile(statePath())
	if err != nil {
		return
	}
	backupBytes(prefix, data)
}

func safeName(s string) string {
	replacer := strings.NewReplacer("\\", "_", "/", "_", ":", "_", "*", "_", "?", "_", "\"", "_", "<", "_", ">", "_", "|", "_", " ", "_")
	return replacer.Replace(s)
}
