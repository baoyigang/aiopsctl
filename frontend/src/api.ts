export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
    public code?: string,
    public currentRevision?: number,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

export async function api<T>(
  path: string,
  options: { method?: string; body?: unknown; signal?: AbortSignal; timeout?: number } = {},
): Promise<T> {
  const controller = new AbortController();
  const abort = () => controller.abort(options.signal?.reason);
  if (options.signal?.aborted) abort();
  else options.signal?.addEventListener('abort', abort, { once: true });
  const timer = window.setTimeout(() => controller.abort('timeout'), options.timeout ?? 15000);
  try {
    const response = await fetch(path, {
      method: options.method ?? 'GET',
      headers: {
        Accept: 'application/json',
        ...(options.body === undefined ? {} : { 'Content-Type': 'application/json' }),
        ...(options.method && options.method !== 'GET' ? { 'X-Hengchuan-Write': '1' } : {}),
      },
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      signal: controller.signal,
      cache: 'no-store',
      credentials: 'same-origin',
    });
    if (!response.ok) {
      const error = (await response.json().catch(() => ({}))) as {
        error?: string;
        message?: string;
        code?: string;
        currentRevision?: number;
      };
      throw new ApiError(
        response.status,
        response.status === 409
          ? `数据冲突：${error.message ?? '版本已更新'}。草稿已保留，请刷新版本并核对后重试。`
          : (error.error ?? error.message ?? `请求未完成（HTTP ${response.status}）`),
        error.code,
        error.currentRevision,
      );
    }
    if (response.status === 204) return undefined as T;
    return (await response.json()) as T;
  } catch (error) {
    if (controller.signal.aborted) {
      if (controller.signal.reason === 'timeout') throw new ApiError(408, '请求超时，请稍后重试。');
      throw new DOMException('操作已取消', 'AbortError');
    }
    if (error instanceof TypeError) throw new ApiError(0, '服务暂不可用，请检查后端连接。');
    throw error;
  } finally {
    window.clearTimeout(timer);
    options.signal?.removeEventListener('abort', abort);
  }
}
