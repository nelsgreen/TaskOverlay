//go:build windows

package main

import (
	"time"
)

type Task struct {
	ID          int64      `json:"id"`
	ParentID    int64      `json:"parent_id"`
	Text        string     `json:"text"`
	Description string     `json:"description,omitempty"`
	Done        bool       `json:"done"`
	Priority    int        `json:"priority"`
	InWork      bool       `json:"in_work"`
	DueHHMM     string     `json:"due_hhmm"`
	Ack         bool       `json:"ack"`
	Blink       bool       `json:"blink"`
	Expanded    bool       `json:"expanded"`
	CreatedAt   time.Time  `json:"created_at"`
	DoneAt      *time.Time `json:"done_at,omitempty"`
}

type Settings struct {
	X                   int32  `json:"x"`
	Y                   int32  `json:"y"`
	W                   int32  `json:"w"`
	H                   int32  `json:"h"`
	BgColor             uint32 `json:"bg_color"`
	TextColor           uint32 `json:"text_color"`
	BorderColor         uint32 `json:"border_color"`
	Alpha               byte   `json:"alpha"`
	BgAlpha             byte   `json:"bg_alpha"`
	TextAlpha           byte   `json:"text_alpha"`
	FontSize            int32  `json:"font_size"`
	Bold                bool   `json:"bold"`
	Shadow              bool   `json:"shadow"`
	Outline             bool   `json:"outline"`
	ShowBorders         bool   `json:"show_borders"`
	DoneStyle           int    `json:"done_style"`
	CollapseDone        bool   `json:"collapse_done"`
	CompletedExpanded   bool   `json:"completed_expanded"`
	PassiveMarkerStyle  string `json:"passive_marker_style"`
	ShowCompletedActive bool   `json:"show_completed_active"`
	AutoHideDelayMS     int    `json:"auto_hide_delay_ms"`
}

type State struct {
	SchemaVersion int      `json:"schema_version"`
	AppVersion    string   `json:"app_version"`
	NextID        int64    `json:"next_id"`
	Tasks         []Task   `json:"tasks"`
	Settings      Settings `json:"settings"`
}

type Action struct {
	Rect   RECT
	Kind   string
	TaskID int64
	Value  int
}

type App struct {
	hwnd                HWND
	state               State
	actions             []Action
	settingsOpen        bool
	settingsPaintLogged bool
	scroll              int32
	blinkOn             bool
	editActive          bool
	editTaskID          int64
	editKind            string
	editText            string
	editOriginal        string
	editReplaceOnType   bool
	editCreatedNew      bool
	detailsTaskID       int64
	dropdown            string
	windowActive        bool
	overlayActive       bool
	mouseInside         bool
	modeChanging        bool
	saveScheduled       bool
	lastSaveReason      string
	sizing              bool
	status              string
	statusUntil         time.Time
}
