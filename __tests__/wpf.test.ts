
const isWindows = process.platform === 'win32';

(isWindows ? describe : describe.skip)('WPF GUI Tests', () => {
  let dotnet: any;
  let System: any;
  let Windows: any;
  let Controls: any;
  let Media: any;

  beforeAll(async () => {
    try {
      dotnet = await import('../src/index.js');
      dotnet.default.load('PresentationFramework');
      dotnet.default.load('PresentationCore');
      dotnet.default.load('WindowsBase');
      System = dotnet.System;
      Windows = System.Windows;
      Controls = Windows.Controls;
      Media = Windows.Media;
    } catch (e) {
      console.log('Skipping WPF tests - load failed:', e);
    }
  });

  afterAll(() => {
    try {
      dotnet?.node_ps1_dotnet?._close();
    } catch {}
  });

  it('should load PresentationFramework assembly', () => {
    expect(Controls).toBeDefined();
    expect(Windows).toBeDefined();
  });

  it('should load PresentationCore assembly', () => {
    expect(Media).toBeDefined();
    expect(Media.FontFamily).toBeDefined();
  });

  it('should create a Window instance', () => {
    const window = new Windows.Window();
    expect(window).toBeDefined();
    expect(window.__ref).toBeDefined();
  });

  it('should set Window properties', () => {
    const window = new Windows.Window();
    window.Title = 'Test Window';
    window.Width = 400;
    window.Height = 300;
    window.WindowStartupLocation = Windows.WindowStartupLocation.CenterScreen;
    
    expect(window.Title).toBe('Test Window');
    expect(window.Width).toBe(400);
    expect(window.Height).toBe(300);
  });

  it('should create a StackPanel', () => {
    const stackPanel = new Controls.StackPanel();
    expect(stackPanel).toBeDefined();
    
    stackPanel.HorizontalAlignment = Windows.HorizontalAlignment.Center;
    stackPanel.VerticalAlignment = Windows.VerticalAlignment.Center;
    
    expect(stackPanel.HorizontalAlignment).toBeDefined();
  });

  it('should create a Label control', () => {
    const label = new Controls.Label();
    expect(label).toBeDefined();
    label.Content = 'Test Label';
    label.FontSize = 24;
    label.Margin = new Windows.Thickness(10);
    
    expect(label.Content).toBe('Test Label');
    expect(label.FontSize).toBe(24);
  });

  it('should create a Button control', () => {
    const button = new Controls.Button();
    expect(button).toBeDefined();
    
    button.Content = 'Click Me';
    button.FontSize = 14;
    button.Padding = new Windows.Thickness(5);
    
    expect(button.Content).toBe('Click Me');
  });

  it('should add children to StackPanel', () => {
    const stackPanel = new Controls.StackPanel();
    const label = new Controls.Label();
    const button = new Controls.Button();
    
    stackPanel.Children.Add(label);
    stackPanel.Children.Add(button);
    
    expect(stackPanel.Children.Count).toBeGreaterThanOrEqual(2);
  });

  it('should set Window Content to StackPanel', () => {
    const window = new Windows.Window();
    const stackPanel = new Controls.StackPanel();
    
    window.Content = stackPanel;
    
    expect(window.Content).toBeDefined();
  });

  it('should register button click event handler', () => {
    const button = new Controls.Button();
    
    button.add_Click((sender: any, e: any) => {
      console.log('Button clicked');
    });
    
    expect(button).toBeDefined();
  });

  it('should create FontFamily', () => {
    const fontFamily = new Media.FontFamily('Arial');
    expect(fontFamily).toBeDefined();
  });

  it('should create Thickness', () => {
    const thickness = new Windows.Thickness(10);
    expect(thickness).toBeDefined();
    
    const thickness2 = new Windows.Thickness(5, 10, 15, 20);
    expect(thickness2).toBeDefined();
  });
});
