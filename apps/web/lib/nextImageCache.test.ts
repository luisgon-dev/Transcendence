import { promises as fs } from "node:fs";
import { createRequire } from "node:module";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { expect, it, vi } from "vitest";

const require = createRequire(import.meta.url);
const { ImageOptimizerCache } = require("next/dist/server/image-optimizer");
const { resetDiskLRU } = require("next/dist/server/lib/disk-lru-cache.external");

it("serves cached images while the eviction index rebuilds, then resumes bounded writes", async () => {
  const distDir = await fs.mkdtemp(join(tmpdir(), "trn-image-cache-"));
  const images = join(distDir, "cache", "images");
  const hitDir = join(images, "cached");
  const missDir = join(images, "new");
  const expiry = Date.now() + 60_000;
  let resumeIndex = () => {};
  const indexGate = new Promise<void>(resolve => { resumeIndex = resolve; });
  const readDir = fs.readdir.bind(fs);

  try {
    await fs.mkdir(hitDir, { recursive: true });
    await fs.writeFile(join(hitDir, `60.${expiry}.etag.upstream.webp`), "one");
    const scan = vi.spyOn(fs, "readdir").mockImplementation((async (path: string) => {
      if (path === images) await indexGate;
      return readDir(path);
    }) as typeof fs.readdir);
    const cache = new ImageOptimizerCache({
      distDir,
      nextConfig: { images: { maximumDiskCacheSize: 6 }, experimental: { isrFlushToDisk: true } }
    });
    const image = { kind: "IMAGE", buffer: Buffer.from("four"), extension: "webp", etag: "etag", upstreamEtag: "upstream" };
    const put = () => cache.set("new", image, { cacheControl: { revalidate: 60 } });

    let served = false;
    const requests = Promise.all([cache.get("cached"), put()]).then(([hit]) => {
      served = true;
      return hit;
    });
    try {
      await vi.waitFor(() => expect(served).toBe(true), { timeout: 1_000 });
      expect(scan).toHaveBeenCalledWith(images);
      expect((await requests).value.buffer.toString()).toBe("one");
      await expect(readDir(missDir)).rejects.toMatchObject({ code: "ENOENT" });
    } finally {
      resumeIndex();
      await requests;
    }

    await vi.waitFor(async () => {
      if (!(await cache.get("new"))) await put();
      expect((await cache.get("new"))?.value.buffer.toString()).toBe("four");
      expect(await cache.get("cached")).toBeNull();
    });
  } finally {
    vi.restoreAllMocks();
    resetDiskLRU();
    await fs.rm(distDir, { recursive: true, force: true });
  }
});
