import { createReadStream, existsSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import { extname, isAbsolute, join, normalize, relative } from 'node:path';

const root = normalize(join(process.cwd(), 'build', 'api'));
const contentTypes = new Map([
  ['.css', 'text/css'],
  ['.html', 'text/html'],
  ['.js', 'text/javascript'],
  ['.json', 'application/json'],
  ['.svg', 'image/svg+xml'],
  ['.woff2', 'font/woff2'],
]);

createServer((request, response) => {
  const requestPath = decodeURIComponent(new URL(request.url ?? '/', 'http://localhost').pathname);
  const relativePath = requestPath === '/' ? 'index.html' : requestPath.slice(1);
  const filePath = normalize(join(root, relativePath));
  const pathFromRoot = relative(root, filePath);

  if (pathFromRoot.startsWith('..') || isAbsolute(pathFromRoot) || !existsSync(filePath) || !statSync(filePath).isFile()) {
    response.writeHead(404).end();
    return;
  }

  response.setHeader('Content-Type', contentTypes.get(extname(filePath)) ?? 'application/octet-stream');
  createReadStream(filePath).pipe(response);
}).listen(3001, '127.0.0.1');
