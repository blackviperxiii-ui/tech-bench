//! Local settings: last job and window (`settings.json`), adapter/page prefs
//! (`adapter-prefs.json`), and the load gate that keeps startup from wiping them
//! (port of `src/Core/Settings/{AppSettings,AdapterPrefs,SettingsStore,SettingsWriteGate}.cs`).

pub mod atomic;
pub mod shop;
pub mod store;

pub use shop::ShopSettings;
pub use store::{match_work_order_key, AdapterPrefsData, AppSettings, SettingsStore};
