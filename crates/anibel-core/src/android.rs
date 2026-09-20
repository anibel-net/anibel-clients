//! Android transports UTF-8 bytes through the existing C ABI.
use crate::ffi;
use jni::{
    JNIEnv,
    objects::{JByteArray, JClass},
    sys::{jbyteArray, jint, jlong},
};
use std::ffi::{CStr, CString};

fn input(env: &JNIEnv, bytes: JByteArray) -> Option<CString> {
    CString::new(env.convert_byte_array(bytes).ok()?).ok()
}

#[unsafe(no_mangle)]
pub extern "system" fn Java_net_anibel_app_CoreNative_init(
    env: JNIEnv,
    _: JClass,
    config: JByteArray,
) -> jlong {
    let Some(config) = input(&env, config) else {
        return -1;
    };
    unsafe { ffi::anibel_core_init(config.as_ptr()) }
}

#[unsafe(no_mangle)]
pub extern "system" fn Java_net_anibel_app_CoreNative_begin(
    _: JNIEnv,
    _: JClass,
    handle: jlong,
    id: jlong,
) -> jint {
    ffi::anibel_core_request_begin(handle, id)
}

#[unsafe(no_mangle)]
pub extern "system" fn Java_net_anibel_app_CoreNative_cancel(
    _: JNIEnv,
    _: JClass,
    handle: jlong,
    id: jlong,
) {
    ffi::anibel_core_cancel(handle, id);
}

#[unsafe(no_mangle)]
pub extern "system" fn Java_net_anibel_app_CoreNative_shutdown(
    _: JNIEnv,
    _: JClass,
    handle: jlong,
) {
    unsafe { ffi::anibel_core_shutdown(handle) };
}

#[unsafe(no_mangle)]
pub extern "system" fn Java_net_anibel_app_CoreNative_call(
    env: JNIEnv,
    _: JClass,
    handle: jlong,
    request: JByteArray,
) -> jbyteArray {
    let Some(request) = input(&env, request) else {
        return std::ptr::null_mut();
    };
    unsafe {
        let response = ffi::anibel_core_call(handle, request.as_ptr());
        if response.is_null() {
            return std::ptr::null_mut();
        }
        let result = env.byte_array_from_slice(CStr::from_ptr(response).to_bytes());
        ffi::anibel_core_free(response);
        result
            .map(|array| array.into_raw())
            .unwrap_or(std::ptr::null_mut())
    }
}
