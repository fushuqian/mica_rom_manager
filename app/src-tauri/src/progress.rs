use crate::state::AppState;
use serde_json::json;
use std::time::{Duration, Instant};
use tauri::{AppHandle, Emitter};

pub const PROGRESS_EVENT: &str = "task://progress";
pub const DONE_EVENT: &str = "task://done";

/// 长任务进度回调：返回 true 表示用户已请求取消。
pub trait TaskReporter {
    fn tick(&mut self, done: usize, total: usize, message: &str) -> bool;
}

/// 单测等无界面场景用。
#[cfg(test)]
pub struct NoopReporter;

#[cfg(test)]
impl TaskReporter for NoopReporter {
    fn tick(&mut self, _done: usize, _total: usize, _message: &str) -> bool {
        false
    }
}

pub struct EmitReporter<'a> {
    app: &'a AppHandle,
    st: &'a AppState,
    task: &'static str,
    last: Instant,
    interval: Duration,
}

impl<'a> EmitReporter<'a> {
    fn new(app: &'a AppHandle, st: &'a AppState, task: &'static str) -> Self {
        Self {
            app,
            st,
            task,
            last: Instant::now() - Duration::from_secs(1),
            interval: Duration::from_millis(120),
        }
    }
}

impl TaskReporter for EmitReporter<'_> {
    fn tick(&mut self, done: usize, total: usize, message: &str) -> bool {
        let cancelled = self.st.cancelled();
        if cancelled || done == total || self.last.elapsed() >= self.interval {
            self.last = Instant::now();
            let _ = self.app.emit(
                PROGRESS_EVENT,
                json!({
                    "task": self.task,
                    "done": done,
                    "total": total,
                    "message": message,
                    "cancelled": cancelled,
                }),
            );
        }
        cancelled
    }
}

/// 任务生命周期守卫：开始时清取消标记并广播 0 进度，结束时广播 done。
pub struct TaskGuard<'a> {
    app: &'a AppHandle,
    st: &'a AppState,
    task: &'static str,
}

impl<'a> TaskGuard<'a> {
    pub fn new(app: &'a AppHandle, st: &'a AppState, task: &'static str) -> Self {
        st.begin_task();
        let _ = app.emit(
            PROGRESS_EVENT,
            json!({ "task": task, "done": 0, "total": 0, "message": "准备中…", "cancelled": false }),
        );
        Self { app, st, task }
    }

    pub fn reporter(&self) -> EmitReporter<'a> {
        EmitReporter::new(self.app, self.st, self.task)
    }
}

impl Drop for TaskGuard<'_> {
    fn drop(&mut self) {
        self.st.end_task();
        let _ = self.app.emit(DONE_EVENT, json!({ "task": self.task }));
    }
}
