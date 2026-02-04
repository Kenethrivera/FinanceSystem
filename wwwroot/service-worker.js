// wwwroot/service-worker.js

const CACHE_NAME = 'pfc-olivarez-finance-v1';
const OFFLINE_URL = '/offline.html';

// 1. Install Event: Cache critical files
self.addEventListener('install', (event) => {
    event.waitUntil(
        caches.open(CACHE_NAME).then((cache) => {
            return cache.addAll([
                '/',
                '/css/site.css',
                '/lib/bootstrap/dist/css/bootstrap.min.css',
                '/images/pfc-logo.jpg',
                '/js/site.js'
            ]);
        })
    );
});

// 2. Fetch Event: Serve from cache if offline
self.addEventListener('fetch', (event) => {
    event.respondWith(
        fetch(event.request).catch(() => {
            return caches.match(event.request);
        })
    );
});