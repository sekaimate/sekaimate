interface WorkerConfigOptions {
  workerName: string;
  workerScript: string;
  compatibilityDate: string;
  domain: string;
  bucketName: string;
  ssoConfigUrl: string;
}

export function createWorkerConfig(options: WorkerConfigOptions) {
  return {
    name: options.workerName,
    main: options.workerScript,
    compatibility_date: options.compatibilityDate,
    workers_dev: false,
    cache: { enabled: true },
    routes: [{ pattern: options.domain, custom_domain: true }],
    r2_buckets: [{ binding: 'WEB_BUILD', bucket_name: options.bucketName }],
    vars: { SSO_CONFIG_URL: options.ssoConfigUrl },
  };
}
