import { AssetLinkApiError, type AssetLinkClient } from "./assetLinkClient";
import { imageFailure, type DerivedImage, type ImageVariant } from "./imageResponses";

export type ImageState =
  | { status: "idle" | "loading" }
  | { status: "ready"; url: string; width: number; height: number }
  | { status: "error"; message: string; statusCode: number | null };

interface ImageJob {
  libraryId: string;
  entryId: string;
  variant: ImageVariant;
  controller: AbortController;
  notify: (state: ImageState) => void;
  timer: number;
  expiresAt: number;
  active: boolean;
  released: boolean;
  url: string | null;
  bytes: number;
  pixels: number;
}

// A workspace owns only visible leases, never an offscreen or persistent image cache.
export class ImageRequests {
  private jobs = new Set<ImageJob>();
  private denied = new Map<string, number>();
  private running = 0;
  private bytes = 0;
  private pixels = 0;

  public constructor(
    private readonly client: AssetLinkClient,
    private readonly accessLost: (status: number, libraryId: string) => void,
  ) {}

  public acquire(libraryId: string, entryId: string, variant: ImageVariant, notify: (state: ImageState) => void) {
    const status = this.denied.get(libraryId);
    if (status !== undefined) {
      notify(errorState(imageFailure(status)));
      return () => undefined;
    }
    if (this.jobs.size >= (variant === "thumbnail" ? 47 : 48)) {
      notify(errorState(imageFailure(429, "preview_busy")));
      return () => undefined;
    }
    const job: ImageJob = {
      libraryId,
      entryId,
      variant,
      notify,
      controller: new AbortController(),
      timer: 0,
      expiresAt: performance.now() + 20_000,
      active: false,
      released: false,
      url: null,
      bytes: 0,
      pixels: 0,
    };
    job.timer = window.setTimeout(() => this.expire(job), 20_000);
    this.jobs.add(job);
    notify({ status: "loading" });
    this.pump();
    return () => {
      job.released = true;
      this.free(job);
      this.jobs.delete(job);
      this.pump();
    };
  }

  public dispose() {
    for (const job of this.jobs) {
      job.released = true;
      this.free(job);
    }
    this.jobs.clear();
    this.denied.clear();
  }

  private pump() {
    // A freed slot may run before another job's due timer callback. Check elapsed time at admission too.
    for (const job of this.jobs) {
      if (!job.active && performance.now() >= job.expiresAt) this.expire(job);
    }
    while (this.running < 2) {
      const waiting = [...this.jobs].filter((job) => !job.active && !job.controller.signal.aborted && !job.released);
      const job = waiting.find((item) => item.variant === "preview") ?? waiting[0];
      if (!job) return;
      job.active = true;
      this.running++;
      void this.run(job);
    }
  }

  private expire(job: ImageJob) {
    if (job.released || job.controller.signal.aborted) return;
    this.free(job);
    job.notify(errorState(imageFailure(504, "preview_timeout")));
    if (!job.active) this.jobs.delete(job);
  }

  private async run(job: ImageJob) {
    try {
      const image = await this.client.getImage(job.libraryId, job.entryId, job.variant, job.controller.signal);
      job.controller.signal.throwIfAborted();
      if (job.released) return;
      const pixels = image.width * image.height;
      if (this.bytes + image.blob.size > 33_554_432 || this.pixels + pixels > 12_000_000)
        throw imageFailure(422, "preview_limit_exceeded");
      job.bytes = image.blob.size;
      job.pixels = pixels;
      this.bytes += job.bytes;
      this.pixels += pixels;
      job.url = URL.createObjectURL(image.blob);
      await checkDecode(job.url, image, job.controller.signal);
      if (!job.released && !job.controller.signal.aborted)
        job.notify({ status: "ready", url: job.url, width: image.width, height: image.height });
    } catch (error: unknown) {
      if (!job.released) {
        if (error instanceof AssetLinkApiError && [401, 403, 404].includes(error.status)) {
          this.denied.set(job.libraryId, error.status);
          for (const affected of this.jobs) {
            if (error.status === 401 || affected.libraryId === job.libraryId) {
              this.free(affected);
              affected.notify(errorState(error));
            }
          }
          this.accessLost(error.status, job.libraryId);
        } else if (!job.controller.signal.aborted) {
          this.free(job);
          job.notify(errorState(error));
        }
      }
    } finally {
      window.clearTimeout(job.timer);
      this.running--;
      this.pump();
    }
  }

  private free(job: ImageJob) {
    job.controller.abort();
    window.clearTimeout(job.timer);
    if (job.url) URL.revokeObjectURL(job.url);
    job.url = null;
    this.bytes -= job.bytes;
    this.pixels -= job.pixels;
    job.bytes = 0;
    job.pixels = 0;
  }
}

function errorState(error: unknown): ImageState {
  return {
    status: "error",
    message: error instanceof AssetLinkApiError ? error.message : "图片无法显示，请重试。",
    statusCode: error instanceof AssetLinkApiError ? error.status : null,
  };
}

function checkDecode(url: string, expected: DerivedImage, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    signal.throwIfAborted();
    const image = new Image();
    const cancel = () => {
      image.src = "";
      reject(signal.reason);
    };
    signal.addEventListener("abort", cancel, { once: true });
    image.src = url;
    void image
      .decode()
      .then(() => {
        signal.throwIfAborted();
        if (image.naturalWidth !== expected.width || image.naturalHeight !== expected.height)
          throw imageFailure(422, "preview_invalid");
        resolve();
      })
      .catch(() => reject(signal.aborted ? signal.reason : imageFailure(422, "preview_invalid")))
      .finally(() => {
        signal.removeEventListener("abort", cancel);
        image.src = "";
      });
  });
}
